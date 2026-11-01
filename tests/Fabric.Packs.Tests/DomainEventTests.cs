namespace Fabric.Packs.Tests;

/// <summary>module.style.ddd: domain events reach in-process handlers after the save, and every event has one.</summary>
public sealed class DomainEventTests
{
    private const string Profile = "saas-standard-react";
    private const string DatabasePassword = "fabric-tests";

    private const string Invoice = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Invoices;

        public sealed record InvoiceId(Guid Value);

        public sealed record InvoicePaid(InvoiceId Invoice) : IDomainEvent;

        public sealed record InvoiceVoided(InvoiceId Invoice) : IDomainEvent;

        public sealed record InvoiceSettled(InvoiceId Invoice) : IDomainEvent;

        public sealed class Invoice(InvoiceId id) : AggregateRoot<InvoiceId>(id)
        {
            public bool IsPaid { get; private set; }

            public bool IsVoided { get; private set; }

            public void Pay()
            {
                IsPaid = true;
                Raise(new InvoicePaid(Id));
            }

            public void Void()
            {
                IsVoided = true;
                Raise(new InvoiceVoided(Id));
            }

            public void Settle()
            {
                IsPaid = true;
                Raise(new InvoiceSettled(Id));
            }
        }
        """;

    private const string InvoiceConfiguration = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Infrastructure.Persistence.Configurations;

        public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
        {
            public void Configure(EntityTypeBuilder<Invoice> builder)
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Property(invoice => invoice.Id).HasConversion(id => id.Value, value => new InvoiceId(value));
                builder.Property(invoice => invoice.IsPaid);
                builder.Property(invoice => invoice.IsVoided);
            }
        }
        """;

    private const string Handlers = """
        using System.Collections.Concurrent;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Shop.BuildingBlocks.DomainEvents;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Application.Invoices;

        public static class Recorded
        {
            public static ConcurrentBag<(InvoicePaid Event, IServiceProvider Services)> Paid { get; } = [];

            public static ConcurrentBag<(InvoiceId Settled, InvoiceId Receipt)> Settled { get; } = [];
        }

        public sealed class InvoicePaidHandler(IServiceProvider services) : IDomainEventHandler<InvoicePaid>
        {
            public Task HandleAsync(InvoicePaid domainEvent, CancellationToken cancellationToken)
            {
                Recorded.Paid.Add((domainEvent, services));
                return Task.CompletedTask;
            }
        }

        public sealed class InvoiceVoidedHandler : IDomainEventHandler<InvoiceVoided>
        {
            public Task HandleAsync(InvoiceVoided domainEvent, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The voided handler failed.");
        }

        /// <summary>Saves another aggregate through the module's context, which the Application cannot name; a real handler goes through a port.</summary>
        public sealed class InvoiceSettledHandler(IServiceProvider services) : IDomainEventHandler<InvoiceSettled>
        {
            public async Task HandleAsync(InvoiceSettled domainEvent, CancellationToken cancellationToken)
            {
                Ensure.That(domainEvent).IsNotNull();

                var contextType = Type.GetType("Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext, Shop.Modules.Billing.Infrastructure", throwOnError: true)!;
                var context = (DbContext)services.GetRequiredService(contextType);
                var receipt = new Invoice(new InvoiceId(Guid.CreateVersion7()));
                context.Add(receipt);
                receipt.Pay();
                await context.SaveChangesAsync(cancellationToken);
                Recorded.Settled.Add((domainEvent.Invoice, receipt.Id));
            }
        }
        """;

    private const string DispatchTests = """
        using System.Transactions;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Shop.BuildingBlocks.DomainEvents;
        using Shop.BuildingBlocks.Tenancy;
        using Shop.Modules.Billing.Api;
        using Shop.Modules.Billing.Application.Invoices;
        using Shop.Modules.Billing.Domain.Invoices;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Tests;

        public class DomainEventTests
        {
            [Fact]
            public async Task An_event_reaches_its_handler_once_after_the_save_in_a_scope_of_its_own()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var invoice = Add(context);

                invoice.Pay();
                await context.SaveChangesAsync(cancellationToken);

                var (_, services) = Recorded.Paid.Where(paid => paid.Event.Invoice == invoice.Id).ShouldHaveSingleItem();
                services.ShouldNotBeSameAs(scope.ServiceProvider);
                invoice.DomainEvents.ShouldBeEmpty();

                await context.SaveChangesAsync(cancellationToken);
                Delivered(invoice).ShouldBe(1);
            }

            [Fact]
            public async Task An_event_saved_in_a_transaction_waits_for_the_commit()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var invoice = Add(context);

                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                invoice.Pay();
                await context.SaveChangesAsync(cancellationToken);
                Delivered(invoice).ShouldBe(0);

                await transaction.CommitAsync(cancellationToken);
                Delivered(invoice).ShouldBe(1);
            }

            [Fact]
            public async Task An_event_saved_in_a_transaction_that_rolls_back_or_is_abandoned_is_never_delivered()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var rolledBack = Add(context);

                await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
                {
                    rolledBack.Pay();
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.RollbackAsync(cancellationToken);
                }

                var abandoned = Add(context);
                await using (await context.Database.BeginTransactionAsync(cancellationToken))
                {
                    abandoned.Pay();
                    await context.SaveChangesAsync(cancellationToken);
                }

                var later = Add(context);
                later.Pay();
                await context.SaveChangesAsync(cancellationToken);

                Delivered(rolledBack).ShouldBe(0);
                Delivered(abandoned).ShouldBe(0);
                Delivered(later).ShouldBe(1);
            }

            [Fact]
            public async Task A_save_that_did_not_accept_its_changes_and_rolled_back_delivers_its_event_when_it_is_retried()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var invoice = Add(context);
                invoice.Pay();

                await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
                {
                    await context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
                    await transaction.RollbackAsync(cancellationToken);
                }

                Delivered(invoice).ShouldBe(0);
                await context.SaveChangesAsync(cancellationToken);
                Delivered(invoice).ShouldBe(1);
            }

            [Fact]
            public async Task A_synchronous_save_with_events_fails_before_anything_is_saved()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var invoice = Add(context);
                invoice.Pay();

                Should.Throw<InvalidOperationException>(() => context.SaveChanges());

                (await CountAsync(provider, invoice, cancellationToken)).ShouldBe(0);
                await context.SaveChangesAsync(cancellationToken);
                Delivered(invoice).ShouldBe(1);
            }

            [Fact]
            public async Task A_save_with_events_in_an_ambient_transaction_or_after_a_savepoint_fails_before_anything_is_saved()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using (var scope = provider.CreateAsyncScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                    var invoice = Add(context);
                    invoice.Pay();

                    using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                    {
                        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync(cancellationToken));
                    }

                    (await CountAsync(provider, invoice, cancellationToken)).ShouldBe(0);
                }

                await using (var scope = provider.CreateAsyncScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                    var invoice = Add(context);

                    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                    await transaction.CreateSavepointAsync("before-paying", cancellationToken);
                    invoice.Pay();

                    await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync(cancellationToken));
                }
            }

            [Fact]
            public async Task A_handler_that_throws_fails_the_caller_after_the_changes_are_committed()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var invoice = Add(context);
                invoice.Void();

                var failure = await Should.ThrowAsync<DomainEventDispatchException>(() => context.SaveChangesAsync(cancellationToken));

                failure.InnerException.ShouldNotBeNull().Message.ShouldBe("The voided handler failed.");
                await using var check = provider.CreateAsyncScope();
                (await check.ServiceProvider.GetRequiredService<BillingDbContext>().Set<Invoice>().SingleAsync(row => row.Id == invoice.Id, cancellationToken))
                    .IsVoided.ShouldBeTrue();
            }

            [Fact]
            public async Task A_handler_saves_through_its_own_context_after_the_commit_and_its_events_are_delivered_too()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var tenant = TenantScope.Begin("tenant-a");
                await using var provider = await StartAsync(cancellationToken);
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var invoice = Add(context);

                await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
                {
                    invoice.Settle();
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }

                var (_, receipt) = Recorded.Settled.Where(settled => settled.Settled == invoice.Id).ShouldHaveSingleItem();
                Recorded.Paid.Count(paid => paid.Event.Invoice == receipt).ShouldBe(1);
                (await CountAsync(provider, receipt, cancellationToken)).ShouldBe(1);
            }

            private static async Task<ServiceProvider> StartAsync(CancellationToken cancellationToken)
            {
                var services = new ServiceCollection().AddLogging().AddTenancy();
                var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                new BillingModule().AddServices(services, configuration);
                var provider = services.BuildServiceProvider();
                await using var scope = provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.EnsureCreatedAsync(cancellationToken);
                return provider;
            }

            private static Invoice Add(BillingDbContext context)
            {
                var invoice = new Invoice(new InvoiceId(Guid.CreateVersion7()));
                context.Add(invoice);
                return invoice;
            }

            private static int Delivered(Invoice invoice) => Recorded.Paid.Count(paid => paid.Event.Invoice == invoice.Id);

            private static async Task<int> CountAsync(ServiceProvider provider, Invoice invoice, CancellationToken cancellationToken) =>
                await CountAsync(provider, invoice.Id, cancellationToken);

            private static async Task<int> CountAsync(ServiceProvider provider, InvoiceId invoice, CancellationToken cancellationToken)
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Set<Invoice>().CountAsync(row => row.Id == invoice, cancellationToken);
            }
        }
        """;

    [Fact]
    public void A_domain_event_reaches_its_handlers_after_the_save_and_never_from_a_transaction_that_rolls_back()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Invoice.cs", Invoice);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/Configurations/InvoiceConfiguration.cs", InvoiceConfiguration);
        project.Write("src/Modules/Billing/Billing.Application/Invoices/InvoiceHandlers.cs", Handlers);
        project.Write("tests/Modules/Billing.Tests/DomainEventTests.cs", DispatchTests);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 9");
    }

    [Fact]
    public void A_domain_event_without_a_handler_the_dispatcher_delivers_to_fails_the_architecture_tests_unless_marked_unhandled()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/InvoiceEvents.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoicePaid(Guid Invoice) : IDomainEvent;

            public sealed record InvoiceSent(Guid Invoice) : IDomainEvent;

            [DeliberatelyUnhandled("Only the invoice's own history needs it.")]
            public sealed record InvoiceArchived(Guid Invoice) : IDomainEvent;

            [DeliberatelyUnhandled("Nobody is meant to react.")]
            public sealed record InvoiceDisputed(Guid Invoice) : IDomainEvent;

            public record InvoiceCorrected(Guid Invoice) : IDomainEvent;

            public sealed record InvoiceReminded(Guid Invoice) : IDomainEvent;

            public abstract record InvoiceEvent(Guid Invoice) : IDomainEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Application/Invoices/InvoiceHandlers.cs", """
            using Shop.BuildingBlocks.Domain;
            using Shop.BuildingBlocks.DomainEvents;
            using Shop.Modules.Billing.Domain.Invoices;

            namespace Shop.Modules.Billing.Application.Invoices;

            public sealed class InvoicePaidHandler : IDomainEventHandler<InvoicePaid>
            {
                public Task HandleAsync(InvoicePaid domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class InvoiceDisputedHandler : IDomainEventHandler<InvoiceDisputed>
            {
                public Task HandleAsync(InvoiceDisputed domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class InvoiceCorrectedHandler : IDomainEventHandler<InvoiceCorrected>
            {
                public Task HandleAsync(InvoiceCorrected domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class EveryEventHandler : IDomainEventHandler<IDomainEvent>
            {
                public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/InvoiceRemindedHandler.cs", """
            using Shop.BuildingBlocks.DomainEvents;
            using Shop.Modules.Billing.Domain.Invoices;

            namespace Shop.Modules.Billing.Infrastructure;

            public sealed class InvoiceRemindedHandler : IDomainEventHandler<InvoiceReminded>
            {
                public Task HandleAsync(InvoiceReminded domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceSent has no handler");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceReminded has no handler");
        output.ShouldContain("Shop.Modules.Billing.Infrastructure.InvoiceRemindedHandler is outside an Application project");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceCorrected is not sealed");
        output.ShouldContain("Shop.Modules.Billing.Application.Invoices.EveryEventHandler handles IDomainEvent, which is not a sealed domain event");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceDisputed is marked deliberately unhandled, but Shop.Modules.Billing.Application.Invoices.InvoiceDisputedHandler handles it");
        output.ShouldNotContain("InvoicePaid ");
        output.ShouldNotContain("InvoiceArchived");
        output.ShouldNotContain("InvoiceEvent ");
    }

    [Fact]
    public void Handled_domain_events_and_ones_marked_unhandled_keep_the_architecture_rules_green()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/InvoiceEvents.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoicePaid(Guid Invoice) : IDomainEvent;

            [DeliberatelyUnhandled("Only the invoice's own history needs it.")]
            public sealed record InvoiceArchived(Guid Invoice) : IDomainEvent;

            public abstract record InvoiceEvent(Guid Invoice) : IDomainEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Application/Invoices/InvoicePaidHandler.cs", """
            using Shop.BuildingBlocks.DomainEvents;
            using Shop.Modules.Billing.Domain.Invoices;

            namespace Shop.Modules.Billing.Application.Invoices;

            public sealed class InvoicePaidHandler : IDomainEventHandler<InvoicePaid>
            {
                public Task HandleAsync(InvoicePaid domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }
}

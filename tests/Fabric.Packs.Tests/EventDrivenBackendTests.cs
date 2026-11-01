using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>
/// The backend of industrial-event-driven-mfe: Kafka as transport, event-sourced modules on Marten, physical CQRS.
/// Its frontend packs (orval, tanstack-query, micro-frontends) are not written yet (M2-02b, M2-05), and the auth client
/// needs an API client to contribute to, so these tests take the frontend's data layer from saas-standard-react.
/// backend.tenancy=none has no pack yet either (M2-02a); a stand-in, which contributes nothing, takes its place.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class EventDrivenBackendTests
{
    private const string Profile = "industrial-event-driven-mfe";
    private const string DatabasePassword = "fabric-tests";

    private static readonly string[] Frontend = ["frontend.state=redux-toolkit", "frontend.api-client=rtk-query-codegen"];

    private const string Invoice = """
        using Shop.BuildingBlocks.EventSourcing;

        namespace Shop.Modules.Billing.Domain.Invoices;

        public sealed record InvoiceId(Guid Value) : IAggregateId;

        public sealed record InvoiceIssued(Guid Invoice, decimal Amount) : IDomainEvent;

        public sealed record InvoicePaid(Guid Invoice) : IDomainEvent;

        public sealed class Invoice : EventSourcedAggregate<InvoiceId>
        {
            private Invoice()
            {
            }

            public decimal Amount { get; private set; }

            public bool IsPaid { get; private set; }

            public static Invoice Issue(InvoiceId id, decimal amount)
            {
                Ensure.That(id).IsNotNull();

                var invoice = new Invoice();
                invoice.Raise(new InvoiceIssued(id.Value, amount));
                return invoice;
            }

            public void Pay() => Raise(new InvoicePaid(Id.Value));

            private void Apply(InvoiceIssued issued)
            {
                Id = new InvoiceId(issued.Invoice);
                Amount = issued.Amount;
            }

            private void Apply(InvoicePaid paid) => IsPaid = paid.Invoice == Id.Value;
        }
        """;

    private const string InvoiceSummary = """
        namespace Shop.Modules.Billing.Application.ReadModels;

        public sealed class InvoiceSummary
        {
            public Guid Id { get; set; }

            public decimal Amount { get; set; }

            public bool IsPaid { get; set; }
        }
        """;

    private const string InvoiceSummaryProjection = """
        using Marten.Events.Aggregation;
        using Shop.Modules.Billing.Application.ReadModels;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Infrastructure.Projections;

        public sealed class InvoiceSummaryProjection : SingleStreamProjection<InvoiceSummary, Guid>
        {
            public static InvoiceSummary Create(InvoiceIssued issued)
            {
                Ensure.That(issued).IsNotNull();
                return new() { Id = issued.Invoice, Amount = issued.Amount };
            }

            public static void Apply(InvoicePaid paid, InvoiceSummary summary)
            {
                Ensure.That(paid).IsNotNull();
                Ensure.That(summary).IsNotNull();
                summary.IsPaid = paid.Invoice == summary.Id;
            }
        }
        """;

    private const string EventStoreTests = """
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Shop.Modules.Billing.Api;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Application.ReadModels;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Tests;

        public class EventStoreTests
        {
            [Fact]
            public async Task An_aggregate_is_stored_as_its_events_and_rebuilt_from_them()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var provider = Provider();
                var id = new InvoiceId(Guid.NewGuid());

                await using (var scope = provider.CreateAsyncScope())
                {
                    var invoice = Invoice.Issue(id, 42m);
                    invoice.Pay();
                    await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().SaveAsync(invoice, cancellationToken);
                }

                await using (var scope = provider.CreateAsyncScope())
                {
                    var loaded = await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().LoadAsync<Invoice>(id, cancellationToken);

                    loaded.HasValue.ShouldBeTrue();
                    loaded.Value.Id.ShouldBe(id);
                    loaded.Value.Amount.ShouldBe(42m);
                    loaded.Value.IsPaid.ShouldBeTrue();
                    loaded.Value.Version.ShouldBe(2);
                }
            }

            [Fact]
            public async Task A_change_to_a_rebuilt_aggregate_appends_to_its_stream()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var provider = Provider();
                var id = new InvoiceId(Guid.NewGuid());

                await using (var scope = provider.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().SaveAsync(Invoice.Issue(id, 7m), cancellationToken);
                }

                await using (var scope = provider.CreateAsyncScope())
                {
                    var store = scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>();
                    var invoice = (await store.LoadAsync<Invoice>(id, cancellationToken)).Value;
                    invoice.Pay();
                    await store.SaveAsync(invoice, cancellationToken);
                }

                await using (var scope = provider.CreateAsyncScope())
                {
                    var loaded = (await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().LoadAsync<Invoice>(id, cancellationToken)).Value;
                    loaded.IsPaid.ShouldBeTrue();
                    loaded.Version.ShouldBe(2);
                }
            }

            [Fact]
            public async Task An_aggregate_that_was_never_stored_is_absent()
            {
                await using var provider = Provider();
                await using var scope = provider.CreateAsyncScope();

                var loaded = await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>()
                    .LoadAsync<Invoice>(new InvoiceId(Guid.NewGuid()), TestContext.Current.CancellationToken);

                loaded.HasValue.ShouldBeFalse();
            }

            [Fact]
            public async Task A_projection_keeps_the_read_model_a_query_reads()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var provider = Provider();
                var id = new InvoiceId(Guid.NewGuid());

                await using (var scope = provider.CreateAsyncScope())
                {
                    var invoice = Invoice.Issue(id, 13m);
                    invoice.Pay();
                    await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().SaveAsync(invoice, cancellationToken);
                }

                await using (var scope = provider.CreateAsyncScope())
                {
                    var summary = await scope.ServiceProvider.GetRequiredService<IBillingReadModels>().FindAsync<InvoiceSummary>(id.Value, cancellationToken);

                    summary.HasValue.ShouldBeTrue();
                    summary.Value.Amount.ShouldBe(13m);
                    summary.Value.IsPaid.ShouldBeTrue();
                }
            }

            private static ServiceProvider Provider()
            {
                var services = new ServiceCollection().AddLogging();
                var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                new BillingModule().AddServices(services, configuration);
                return services.BuildServiceProvider();
            }
        }
        """;

    [Fact]
    public void The_backend_generates_and_builds_and_a_new_module_passes_its_module_and_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Build();

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs")).ShouldContain("AddKafka(\"messaging\")");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void An_event_sourced_aggregate_is_stored_in_marten_rebuilt_from_its_events_and_projected_to_a_read_model()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Invoice.cs", Invoice);
        project.Write("src/Modules/Billing/Billing.Application/ReadModels/InvoiceSummary.cs", InvoiceSummary);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Projections/InvoiceSummaryProjection.cs", InvoiceSummaryProjection);
        project.Write("tests/Modules/Billing.Tests/EventStoreTests.cs", EventStoreTests);
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);

        using var postgres = Container.Postgres(DatabasePassword);
        var database = Database(postgres);
        var (runnerExitCode, runnerOutput) = project.DotNetCommand(database, "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        runnerExitCode.ShouldBe(0, runnerOutput);

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj", database);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        // The events are the record, in the module's own schema; the read model is a table of its own.
        postgres.Psql(DatabasePassword, "select count(*) from billing.mt_events").ShouldBe("6");
        postgres.Psql(DatabasePassword, "select count(*) from billing.mt_doc_invoicesummary").ShouldBe("3");
    }

    [Fact]
    public void The_migration_runner_creates_the_event_store_and_the_application_never_does()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Invoice.cs", Invoice);
        project.Write("src/Modules/Billing/Billing.Application/ReadModels/InvoiceSummary.cs", InvoiceSummary);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Projections/InvoiceSummaryProjection.cs", InvoiceSummaryProjection);
        project.Write("tests/Modules/Billing.Tests/EventStoreTests.cs", EventStoreTests);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var database = Database(postgres);

        // The host never migrates (ef-core's rule): before the runner, the module finds no tables of its own.
        var (beforeExitCode, beforeOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj", database);
        beforeExitCode.ShouldNotBe(0, beforeOutput);
        beforeOutput.ShouldContain("42P01");

        var (runnerExitCode, runnerOutput) = project.DotNetCommand(database, "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        runnerExitCode.ShouldBe(0, runnerOutput);

        postgres.Psql(
            DatabasePassword,
            "select string_agg(table_name, ',' order by table_name) from information_schema.tables where table_schema = 'billing' and table_name in ('mt_doc_invoicesummary', 'mt_events', 'mt_streams')")
            .ShouldBe("mt_doc_invoicesummary,mt_events,mt_streams");
    }

    [Fact]
    public void A_query_that_takes_the_write_side_as_a_method_parameter_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Queries/GetInvoice/InvoiceQuery.cs", """
            namespace Shop.Modules.Billing.Application.Queries.GetInvoice;

            public static class InvoiceQuery
            {
                public static IBillingAggregateStore Handle(IBillingAggregateStore store) => store;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Application/Queries/ListInvoices/InvoiceList.cs", """
            namespace Shop.Modules.Billing.Application.Queries.ListInvoices;

            public sealed class InvoiceList(TimeProvider clock)
            {
                public Microsoft.EntityFrameworkCore.DbContext Handle(Microsoft.EntityFrameworkCore.DbContext context)
                {
                    _ = clock.GetUtcNow();
                    return context;
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.GetInvoice.InvoiceQuery reads the write side (IBillingAggregateStore)");
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.ListInvoices.InvoiceList reads the write side (DbContext)");
    }

    [Fact]
    public void A_query_reaching_the_write_side_through_a_wrapper_or_a_service_locator_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Queries/Wrapped/WrappedQueries.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            namespace Shop.Modules.Billing.Application.Queries.Wrapped;

            public delegate IBillingAggregateStore StoreSource();

            public sealed class FactoryQuery(IDbContextFactory<DbContext> contexts)
            {
                public IDbContextFactory<DbContext> Contexts { get; } = contexts;
            }

            public static class LazyStoreQuery
            {
                public static Func<IBillingAggregateStore> Handle(Func<IBillingAggregateStore> store) => store;
            }

            public sealed class StoresQuery(IEnumerable<IBillingAggregateStore> stores)
            {
                public IEnumerable<IBillingAggregateStore> Stores { get; } = stores;

                public static IBillingAggregateStore[] Handle(IBillingAggregateStore[] all) => all;
            }

            public static class DelegateQuery
            {
                public static StoreSource Handle(StoreSource source) => source;
            }

            public sealed class LocatorQuery(IServiceProvider services)
            {
                public IServiceProvider Services { get; } = services;
            }

            public static class ScopeQuery
            {
                public static IServiceScopeFactory Handle(IServiceScopeFactory scopes) => scopes;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        const string Queries = "Shop.Modules.Billing.Application.Queries.Wrapped.";
        output.ShouldContain(Queries + "FactoryQuery reads the write side (IDbContextFactory<DbContext>)");
        output.ShouldContain(Queries + "LazyStoreQuery reads the write side (Func<IBillingAggregateStore>)");
        output.ShouldContain(Queries + "StoresQuery reads the write side (IEnumerable<IBillingAggregateStore>)");
        output.ShouldContain(Queries + "StoresQuery reads the write side (IBillingAggregateStore[])");
        output.ShouldContain(Queries + "DelegateQuery reads the write side (StoreSource)");
        output.ShouldContain(Queries + "LocatorQuery takes a service locator (IServiceProvider)");
        output.ShouldContain(Queries + "ScopeQuery takes a service locator (IServiceScopeFactory)");
    }

    [Fact]
    public void A_query_inheriting_the_write_side_from_a_base_class_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Support/StoreHolder.cs", """
            namespace Shop.Modules.Billing.Application.Support;

            public abstract class StoreHolder
            {
                private IBillingAggregateStore? store;

                protected bool HasStore => store is not null;

                protected void Use(IBillingAggregateStore invoices) => store = invoices;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Application/Queries/Inherited/InheritedQuery.cs", """
            using Shop.Modules.Billing.Application.Support;

            namespace Shop.Modules.Billing.Application.Queries.Inherited;

            public sealed class InheritedQuery : StoreHolder
            {
                public bool Ready => HasStore;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.Inherited.InheritedQuery reads the write side (IBillingAggregateStore)");
    }

    [Fact]
    public void An_adapter_over_the_event_store_that_takes_more_than_the_store_is_still_registered()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/IBillingAuditTrail.cs", """
            namespace Shop.Modules.Billing.Application;

            public interface IBillingAuditTrail
            {
                bool Verbose { get; }
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/EventStore/BillingAuditTrail.cs", """
            using Marten;
            using Microsoft.Extensions.Logging;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Infrastructure.EventStore;

            public sealed class BillingAuditTrail(IDocumentStore store, ILogger<BillingAuditTrail> logger) : IBillingAuditTrail
            {
                public IDocumentStore Store { get; } = store;

                public bool Verbose => logger.IsEnabled(LogLevel.Debug);
            }
            """);
        project.Write("tests/Modules/Billing.Tests/AdapterTests.cs", """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Shop.Modules.Billing.Api;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Tests;

            public class AdapterTests
            {
                [Fact]
                public void An_adapter_with_further_dependencies_is_registered_as_its_application_interface()
                {
                    var services = new ServiceCollection().AddLogging();
                    var configuration = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" })
                        .Build();
                    new BillingModule().AddServices(services, configuration);
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();

                    scope.ServiceProvider.GetService<IBillingAuditTrail>().ShouldNotBeNull();
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_read_side_gate_checks_only_modules_that_chose_physical_cqrs()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("ledger");

        // add-module refuses a CQRS option new to the project (its pack writes a project-wide rule), so a logical module
        // beside physical ones is made by hand: ledger loses the read side only the physical pack renders per module.
        File.Delete(Path.Combine(project.ProjectDirectory, "src", "Modules", "Ledger", "Ledger.Application", "ILedgerReadModels.cs"));
        File.Delete(Path.Combine(project.ProjectDirectory, "src", "Modules", "Ledger", "Ledger.Infrastructure", "ReadModels", "LedgerReadModels.cs"));

        project.Write("src/Modules/Ledger/Ledger.Application/Queries/GetEntry/EntryQuery.cs", """
            namespace Shop.Modules.Ledger.Application.Queries.GetEntry;

            public static class EntryQuery
            {
                public static ILedgerAggregateStore Handle(ILedgerAggregateStore store) => store;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Application/Queries/GetInvoice/InvoiceQuery.cs", """
            namespace Shop.Modules.Billing.Application.Queries.GetInvoice;

            public static class InvoiceQuery
            {
                public static IBillingAggregateStore Handle(IBillingAggregateStore store) => store;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.GetInvoice.InvoiceQuery reads the write side (IBillingAggregateStore)");
        output.ShouldNotContain("Shop.Modules.Ledger.Application.Queries.GetEntry.EntryQuery reads the write side");
    }

    [Fact]
    public void A_modules_event_store_lives_in_the_schema_its_dbcontext_uses()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing-2");
        project.Write("src/Modules/Billing2/Billing2.Domain/Notes/NoteAdded.cs", """
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing2.Domain.Notes;

            public sealed record NoteAdded(Guid Note) : IDomainEvent;
            """);
        project.Build();

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "Modules", "Billing2", "Billing2.Infrastructure", "Persistence", "Billing2DbContext.cs"))
            .ShouldContain("public const string Schema = \"billing_2\";");

        using var postgres = Container.Postgres(DatabasePassword);
        var (runnerExitCode, runnerOutput) = project.DotNetCommand(Database(postgres), "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        runnerExitCode.ShouldBe(0, runnerOutput);

        postgres.Psql(DatabasePassword, "select string_agg(table_schema, ',') from information_schema.tables where table_name = 'mt_events'")
            .ShouldBe("billing_2");
    }

    private static Dictionary<string, string> Database(Container postgres) => new()
    {
        ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
    };

    [Fact]
    public void Breaking_the_event_sourcing_rules_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Invoice.cs", Invoice);
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/InvoiceVoided.cs", """
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceVoided(Guid Invoice) : IDomainEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Application/Queries/GetInvoice/InvoiceLookup.cs", """
            namespace Shop.Modules.Billing.Application.Queries.GetInvoice;

            public sealed class InvoiceLookup(IBillingAggregateStore invoices)
            {
                public IBillingAggregateStore Invoices { get; } = invoices;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Application/StoreShortcut.cs", """
            namespace Shop.Modules.Billing.Application;

            public sealed class StoreShortcut(Marten.IDocumentStore store)
            {
                public Marten.IDocumentStore Store { get; } = store;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceVoided is applied by no aggregate");
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.GetInvoice.InvoiceLookup reads the write side");
        output.ShouldContain("Shop.Modules.Billing.Application references Marten");
    }

    [Fact]
    public async Task An_integration_event_published_by_one_module_reaches_a_consumer_in_another_over_kafka()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Contracts.Billing;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api;

            public sealed class PayInvoice : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapPost("/invoices/{id}/pay", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        await unitOfWork.PublishAsync(new InvoicePaidV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using System.Collections.Concurrent;
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                public static ConcurrentBag<Guid> Received { get; } = [];

                public static void Handle(InvoicePaidV1 message) => Received.Add(message.InvoiceId);
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Api/Received.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Shipping.Application;

            namespace Shop.Modules.Shipping.Api;

            public sealed class Received : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/received", () => InvoicePaidConsumer.Received.Count).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var kafka = Container.Kafka();
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["ConnectionStrings__messaging"] = $"localhost:{kafka.Port}",
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        var invoice = Guid.NewGuid();
        (await host.PostAsync($"/billing/invoices/{invoice}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var received = "0";
        for (var attempt = 0; attempt < 120 && received == "0"; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            received = await (await host.GetAsync("/shipping/received")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        received.ShouldBe("1");

        // Without a transport Wolverine would deliver the event in process; it must have crossed on the broker.
        kafka.FirstKafkaMessage("Shop.Contracts.Billing.InvoicePaidV1").ShouldContain(invoice.ToString());
    }

    [Fact]
    public void The_generated_integration_tests_pass_against_the_running_aspire_stack_with_kafka()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }
}

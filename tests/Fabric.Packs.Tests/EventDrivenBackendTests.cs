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
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        // The events are the record, in the module's own schema; the read model is a table of its own.
        postgres.Psql(DatabasePassword, "select count(*) from billing.mt_events").ShouldBe("6");
        postgres.Psql(DatabasePassword, "select count(*) from billing.mt_doc_invoicesummary").ShouldBe("3");
    }

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

using System.Globalization;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>module.event-store.kurrentdb</c> (#254): event-sourced modules keep their events in KurrentDB, which the AppHost
/// runs. The industrial profile with logical CQRS, since physical CQRS requires Marten (M3-05); its frontend data layer
/// comes from saas-standard-react, as in EventDrivenBackendTests.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class KurrentDbTests
{
    private const string Profile = "industrial-event-driven-mfe";
    private const string ClientVersion = "1.6.1";
    private const string HostingVersion = "13.6.1";

    /// <summary>The image the Aspire integration runs, which the Testcontainers fixture runs too.</summary>
    private const string Image = "docker.kurrent.io/kurrent-latest/kurrentdb:26.1.1";

    private static readonly string[] KurrentDb =
        ["frontend.state=redux-toolkit", "frontend.api-client=rtk-query-codegen", "module.event-store=kurrentdb", "module.cqrs=logical"];

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

    private const string EventStoreTests = """
        using KurrentDB.Client;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Shop.Modules.Billing.Api;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Tests;

        public class EventStoreTests
        {
            [Fact]
            public async Task An_aggregate_is_stored_as_its_events_and_rebuilt_from_them()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var provider = Provider();
                var id = new InvoiceId(Guid.CreateVersion7());

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
                var id = new InvoiceId(Guid.CreateVersion7());

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
                    .LoadAsync<Invoice>(new InvoiceId(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

                loaded.HasValue.ShouldBeFalse();
            }

            [Fact]
            public async Task A_save_from_a_copy_another_save_has_overtaken_fails()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var provider = Provider();
                var id = new InvoiceId(Guid.CreateVersion7());
                await using var scope = provider.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>();
                await store.SaveAsync(Invoice.Issue(id, 5m), cancellationToken);
                var first = (await store.LoadAsync<Invoice>(id, cancellationToken)).Value;
                var second = (await store.LoadAsync<Invoice>(id, cancellationToken)).Value;

                first.Pay();
                await store.SaveAsync(first, cancellationToken);
                second.Pay();

                await Should.ThrowAsync<WrongExpectedVersionException>(() => store.SaveAsync(second, cancellationToken));
            }

            [Fact]
            public async Task The_events_are_kept_by_their_type_name_in_one_stream_per_aggregate_in_the_modules_category()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var provider = Provider();
                var id = new InvoiceId(Guid.CreateVersion7());

                await using (var scope = provider.CreateAsyncScope())
                {
                    var invoice = Invoice.Issue(id, 3m);
                    invoice.Pay();
                    await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().SaveAsync(invoice, cancellationToken);
                }

                List<string> types = [];
                await foreach (var stored in provider.GetRequiredService<KurrentDBClient>().ReadStreamAsync(Direction.Forwards, $"billing-{id.Value}", StreamPosition.Start, cancellationToken: cancellationToken))
                {
                    types.Add(stored.Event.EventType);
                    stored.Event.ContentType.ShouldBe("application/json");
                }

                types.ShouldBe(["InvoiceIssued", "InvoicePaid"]);
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

    /// <summary>Issues an invoice and reads it back over HTTP, so the host's event store is the one the AppHost (or the fixture) runs.</summary>
    private const string InvoiceEndpoints = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Api;

        public sealed class InvoiceEndpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPut("/invoices/{id}", async (Guid id, IBillingAggregateStore invoices, CancellationToken cancellationToken) =>
                {
                    await invoices.SaveAsync(Invoice.Issue(new InvoiceId(id), 42m), cancellationToken);
                    return Results.NoContent();
                }).AllowAnonymous();
                endpoints.MapGet("/invoices/{id}", async (Guid id, IBillingAggregateStore invoices, CancellationToken cancellationToken) =>
                {
                    var invoice = await invoices.LoadAsync<Invoice>(new InvoiceId(id), cancellationToken);
                    return invoice.HasValue ? Results.Ok(invoice.Value.Amount) : Results.NotFound();
                }).AllowAnonymous();
            }
        }
        """;

    [Fact]
    public void The_apphost_runs_kurrentdb_and_gives_the_host_its_connection_string()
    {
        using var project = new GeneratedProject(Profile, KurrentDb);
        project.AddModule("billing");

        var appHost = Read(project, "src/AppHost/AppHost.cs");
        appHost.ShouldContain("var eventstore = builder.AddKurrentDB(\"eventstore\")");
        appHost.ShouldContain(".WithReference(eventstore)");
        appHost.ShouldContain(".WaitFor(eventstore)");
        appHost.ShouldNotContain("#pragma");
        Read(project, "src/AppHost/AppHost.csproj").ShouldContain("<PackageReference Include=\"CommunityToolkit.Aspire.Hosting.KurrentDB\" />");
        Read(project, "src/BuildingBlocks/BuildingBlocks.csproj").ShouldContain("<PackageReference Include=\"KurrentDB.Client\" />");

        var versions = Read(project, "Directory.Packages.props");
        versions.ShouldContain($"<PackageVersion Include=\"KurrentDB.Client\" Version=\"{ClientVersion}\" />");
        versions.ShouldContain($"<PackageVersion Include=\"CommunityToolkit.Aspire.Hosting.KurrentDB\" Version=\"{HostingVersion}\" />");
        versions.ShouldNotContain("Marten");

        File.Exists(Path.Combine(project.ProjectDirectory, "src/Modules/Billing/Billing.Infrastructure/EventStore/BillingAggregateStore.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "tests/Architecture.Tests/Rules/OnlyInfrastructureUsesKurrentDb.cs")).ShouldBeTrue();
        Read(project, "AGENTS.md").ShouldContain("## Event store: KurrentDB");
        Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs/adr"), "*-module-event-store-kurrentdb.md").ShouldHaveSingleItem();

        // KurrentDB creates a stream on its first append: the MigrationRunner has nothing to create.
        Read(project, "src/MigrationRunner/Program.cs").ShouldNotContain("EventStore");
    }

    [Fact]
    public void An_event_sourced_module_round_trips_through_kurrentdb_and_passes_its_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, KurrentDb);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", Invoice);
        project.Write("tests/Modules/Billing.Tests/EventStoreTests.cs", EventStoreTests);
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        using var kurrentDb = Container.KurrentDb(Image);
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string>
            {
                ["ConnectionStrings__eventstore"] = $"kurrentdb://127.0.0.1:{kurrentDb.Port.ToString(CultureInfo.InvariantCulture)}?tls=false",
                ["ConnectionStrings__database"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }

    [Fact]
    public void A_module_project_other_than_infrastructure_that_uses_kurrentdb_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, KurrentDb);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/StoreShortcut.cs", """
            namespace Shop.Modules.Billing.Application;

            public sealed class StoreShortcut(KurrentDB.Client.KurrentDBClient client)
            {
                public KurrentDB.Client.KurrentDBClient Client { get; } = client;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application references KurrentDB");
    }

    [Fact]
    public void An_adapter_over_the_event_store_that_takes_more_than_the_store_is_registered_as_its_application_interface()
    {
        using var project = new GeneratedProject(Profile, KurrentDb);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/IBillingAuditTrail.cs", """
            namespace Shop.Modules.Billing.Application;

            public interface IBillingAuditTrail
            {
                bool Verbose { get; }
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/EventStore/BillingAuditTrail.cs", """
            using Microsoft.Extensions.Logging;
            using Shop.BuildingBlocks.EventStore;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Infrastructure.EventStore;

            public sealed class BillingAuditTrail(ModuleEventStore events, ILogger<BillingAuditTrail> logger) : IBillingAuditTrail
            {
                public ModuleEventStore Events { get; } = events;

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
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:database"] = "Host=localhost",
                            ["ConnectionStrings:eventstore"] = "kurrentdb://localhost:2113?tls=false",
                        })
                        .Build();
                    new BillingModule().AddServices(services, configuration);
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();

                    scope.ServiceProvider.GetService<IBillingAuditTrail>().ShouldNotBeNull();
                    scope.ServiceProvider.GetService<IBillingAggregateStore>().ShouldNotBeNull();
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void Two_modules_keep_their_events_in_streams_of_their_own_categories()
    {
        using var project = new GeneratedProject(Profile, KurrentDb);
        project.AddModule("billing");
        project.AddModule("billing-2");

        Read(project, "src/Modules/Billing/Billing.Infrastructure/EventStore/BillingAggregateStore.cs")
            .ShouldContain("public const string Category = \"billing\";");

        // KurrentDB's category is the stream name up to its first hyphen, so a module's own hyphen becomes an underscore.
        Read(project, "src/Modules/Billing2/Billing2.Infrastructure/EventStore/Billing2AggregateStore.cs")
            .ShouldContain("public const string Category = \"billing_2\";");
    }

    /// <summary>The value objects of a strict module are stored as their backing values, as with Marten (the role slot).</summary>
    [Fact]
    public void With_strict_depth_value_objects_are_stored_as_their_backing_values_and_pass_their_guards_when_read()
    {
        using var project = new GeneratedProject(Profile, [.. KurrentDb, "module.ddd-depth=strict"]);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Samples/Sample.cs", """
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Samples;

            public sealed class Sample : EventSourcedAggregate<SampleId>
            {
                private Sample()
                {
                }

                public SampleName Name { get; private set; } = default!;

                public static Sample Create(SampleId id, SampleName name)
                {
                    var sample = new Sample();
                    sample.Raise(new SampleNamed(id, name));
                    return sample;
                }

                private void Apply(SampleNamed named)
                {
                    Id = named.Sample;
                    Name = named.Name;
                }
            }
            """);
        project.Write("src/Modules/Billing/Billing.Domain/Samples/SampleNamed.cs", """
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Samples;

            public sealed record SampleNamed(SampleId Sample, SampleName Name) : IDomainEvent;
            """);
        project.Write("tests/Modules/Billing.Tests/EventStoreTests.cs", """"
            using System.Text;
            using KurrentDB.Client;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Shop.Modules.Billing.Api;
            using Shop.Modules.Billing.Application;
            using Shop.Modules.Billing.Domain.Samples;

            namespace Shop.Modules.Billing.Tests;

            public class EventStoreTests
            {
                [Fact]
                public async Task A_value_object_round_trips_as_its_backing_value()
                {
                    var cancellationToken = TestContext.Current.CancellationToken;
                    await using var provider = Provider();
                    var id = SampleId.New();
                    var store = provider.GetRequiredService<IBillingAggregateStore>();

                    await store.SaveAsync(Sample.Create(id, SampleName.From("Ada")), cancellationToken);

                    (await store.LoadAsync<Sample>(id, cancellationToken)).Value.Name.ShouldBe(SampleName.From("Ada"));
                    await foreach (var stored in provider.GetRequiredService<KurrentDBClient>().ReadStreamAsync(Direction.Forwards, $"billing-{id.Value}", StreamPosition.Start, cancellationToken: cancellationToken))
                    {
                        Encoding.UTF8.GetString(stored.Event.Data.Span).ShouldBe($$$"""{"Sample":"{{{id.Value}}}","Name":"Ada"}""");
                    }
                }

                [Fact]
                public async Task A_stored_value_that_breaks_its_value_objects_guards_fails_to_load()
                {
                    var cancellationToken = TestContext.Current.CancellationToken;
                    await using var provider = Provider();
                    var id = SampleId.New();
                    var stored = new EventData(Uuid.NewUuid(), "SampleNamed", Encoding.UTF8.GetBytes($$$"""{"Sample":"{{{id.Value}}}","Name":"  "}"""), contentType: "application/json");
                    await provider.GetRequiredService<KurrentDBClient>().AppendToStreamAsync($"billing-{id.Value}", StreamState.NoStream, [stored], cancellationToken: cancellationToken);

                    await Should.ThrowAsync<GuardException>(() => provider.GetRequiredService<IBillingAggregateStore>().LoadAsync<Sample>(id, cancellationToken));
                }

                private static ServiceProvider Provider()
                {
                    var services = new ServiceCollection().AddLogging();
                    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                    new BillingModule().AddServices(services, configuration);
                    return services.BuildServiceProvider();
                }
            }
            """");
        project.Build();

        Read(project, "src/BuildingBlocks/EventStore/ModuleEventStore.cs").ShouldContain("json.Converters.Add(new ValueObjectJsonConverterFactory());");
        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);

        using var kurrentDb = Container.KurrentDb(Image);
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string>
            {
                ["ConnectionStrings__eventstore"] = $"kurrentdb://127.0.0.1:{kurrentDb.Port.ToString(CultureInfo.InvariantCulture)}?tls=false",
                ["ConnectionStrings__database"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    /// <summary>
    /// The host's event store is the one the integration tests start: KurrentDB through the AppHost with the Aspire
    /// fixture, in a container of its own with Testcontainers. An invoice issued over HTTP is read back over HTTP.
    /// </summary>
    [Theory]
    [InlineData("aspire-fixture", "AspireFixture aspire", "aspire")]
    [InlineData("testcontainers", "ContainerFixture containers", "containers")]
    public void The_generated_integration_tests_reach_the_hosts_event_store(string integration, string fixture, string field)
    {
        using var project = new GeneratedProject(Profile, [.. KurrentDb, $"backend.test.integration={integration}"]);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", Invoice);
        project.Write("src/Modules/Billing/Billing.Api/InvoiceEndpoints.cs", InvoiceEndpoints);
        project.Write("tests/Integration.Tests/InvoiceTests.cs", $$"""
            using System.Net;
            using System.Net.Http.Json;

            namespace Shop.Integration.Tests;

            public class InvoiceTests({{fixture}})
            {
                [Fact]
                public async Task An_invoice_issued_through_the_host_is_read_back_from_its_events()
                {
                    var cancellationToken = TestContext.Current.CancellationToken;
                    using var client = {{field}}.CreateHostClient();
                    var id = Guid.CreateVersion7();

                    (await client.PutAsync(new Uri($"/billing/invoices/{id}", UriKind.Relative), null, cancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

                    (await client.GetFromJsonAsync<decimal>(new Uri($"/billing/invoices/{id}", UriKind.Relative), cancellationToken)).ShouldBe(42m);
                }
            }
            """);
        project.Build();

        // Only the invoice's tests: its endpoints make the frontend's copy of the OpenAPI document stale by design.
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test",
            "--project",
            "tests/Integration.Tests/Integration.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--no-ansi",
            "--filter-class",
            "Shop.Integration.Tests.InvoiceTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 1");
    }

    [Fact]
    public void With_testcontainers_the_fixture_runs_the_apphosts_kurrentdb_image()
    {
        using var project = new GeneratedProject(Profile, [.. KurrentDb, "backend.test.integration=testcontainers"]);
        project.AddModule("billing");

        var fixture = Read(project, "tests/Integration.Tests/ContainerFixture.cs");
        fixture.ShouldContain($"new ContainerBuilder(\"{Image}\")");
        fixture.ShouldContain("settings[\"ConnectionStrings:eventstore\"]");
    }

    private static string Read(GeneratedProject project, string path) => File.ReadAllText(Path.Combine(project.ProjectDirectory, path));
}

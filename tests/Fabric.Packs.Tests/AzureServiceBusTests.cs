using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.bus.transport.azure-service-bus</c> (M3-22, #248): Wolverine over Azure Service Bus, which the AppHost runs
/// as the emulator. The emulator answers management requests, through which Wolverine creates its queues, on a port of
/// its own, so the host gets a second connection string for them.
/// </summary>
[Collection(DockerPorts.Name)]
public sealed class AzureServiceBusTests
{
    private const string Profile = "saas-standard-react";
    private const string Transport = "backend.bus.transport=azure-service-bus";
    private const string DatabasePassword = "fabric-tests";

    /// <summary>
    /// Publishing the AppHost runs no emulator, so the host gets no emulator address: against Azure, Wolverine manages
    /// its queues through the messaging connection string itself.
    /// </summary>
    private const string PublishModeTests = """
        using Aspire.Hosting;
        using Aspire.Hosting.ApplicationModel;
        using Aspire.Hosting.Testing;

        namespace Shop.Integration.Tests;

        public class PublishModeTests
        {
            [Fact]
            public async Task Publishing_the_apphost_gives_the_host_no_emulator_management_address()
            {
                await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                    ["--operation", "publish", "--publisher", "manifest", "--output-path", Path.GetTempPath()],
                    TestContext.Current.CancellationToken);

                builder.ExecutionContext.IsPublishMode.ShouldBeTrue();
                var host = builder.Resources.Single(resource => resource.Name == "host");
                var environment = new EnvironmentCallbackContext(builder.ExecutionContext, host, [], TestContext.Current.CancellationToken);
                foreach (var callback in host.Annotations.OfType<EnvironmentCallbackAnnotation>())
                {
                    await callback.Callback(environment);
                }

                environment.EnvironmentVariables.Keys.ShouldContain("ConnectionStrings__messaging");
                environment.EnvironmentVariables.Keys.ShouldNotContain("ConnectionStrings__messaging-management");
            }
        }
        """;

    [Fact]
    public void The_apphost_runs_the_emulator_and_the_host_reaches_it_through_wolverines_transport()
    {
        using var project = new GeneratedProject(Profile, Transport);
        project.AddModule("billing");
        project.Build();

        var appHost = File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs"));
        appHost.ShouldContain("AddAzureServiceBus(\"messaging\")");
        appHost.ShouldContain(".RunAsEmulator(");
        appHost.ShouldContain("ConnectionStrings__messaging-management");
        appHost.ShouldNotContain("AddRabbitMQ");

        var versions = File.ReadAllText(Path.Combine(project.ProjectDirectory, "Directory.Packages.props"));
        versions.ShouldContain("<PackageVersion Include=\"Aspire.Hosting.Azure.ServiceBus\" Version=\"13.6.5\" />");
        versions.ShouldContain("<PackageVersion Include=\"WolverineFx.AzureServiceBus\" Version=\"6.64.0\" />");

        var messaging = File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "Host", "Wiring", "Messaging.cs"));
        messaging.ShouldContain("UseAzureServiceBus(");
        messaging.ShouldContain("GetConnectionString(\"messaging-management\")");

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "AGENTS.md")).ShouldContain("## Message transport: Azure Service Bus");
        File.ReadAllText(Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"), "*-backend-bus-transport-azure-service-bus.md").Single())
            .ShouldContain("# Message transport: Azure Service Bus");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public async Task An_integration_event_published_by_one_module_reaches_a_consumer_in_another_over_the_emulator()
    {
        using var project = new GeneratedProject(Profile, Transport);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", SaasStandardReactTests.PayInvoice);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", RedeliveredEvent.Consumer(tenancy: true));
        project.Write("src/Modules/Shipping/Shipping.Api/Deliveries.cs", RedeliveredEvent.Endpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var serviceBus = Container.ServiceBus();
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["ConnectionStrings__messaging"] = Container.ServiceBusConnectionString(serviceBus.Port),
            ["ConnectionStrings__messaging-management"] = Container.ServiceBusConnectionString(serviceBus.ManagementPort!.Value),
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        var publishedAfter = TimeProvider.System.GetUtcNow();
        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await RedeliveredEvent.ShouldHaveBeenDeliveredTwiceWithTheSameMetadataAsync(
            host,
            publishedAfter,
            "tenant-a",
            attempts: 120);

        // Wolverine retried the failed delivery by itself; nothing was dead-lettered on the way.
        RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword).ShouldBe(0);

        // The event itself went through the host's local queue (docs/adr); the host listens to the queue named after it,
        // where another service's InvoicePaidV1 would arrive.
        serviceBus.ServiceBusQueues().ShouldContain("<title type=\"text\">shop.contracts.billing.invoicepaidv1</title>");
    }

    [Fact]
    public async Task An_event_between_modules_of_the_host_survives_the_host_stopping_before_its_consumer_succeeds()
    {
        // The event does not cross the emulator (docs/adr): it is kept in Wolverine's durable local queue, stored with
        // the billing module's changes, so a host killed mid-delivery delivers it after the restart (#688).
        using var project = new GeneratedProject(Profile, Transport);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", SaasStandardReactTests.PayInvoice);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", SurvivingEvent.Consumer);
        project.Write("src/Modules/Shipping/Shipping.Api/Deliveries.cs", SurvivingEvent.Endpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var serviceBus = Container.ServiceBus();
        Dictionary<string, string> dependencies = new()
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["ConnectionStrings__messaging"] = Container.ServiceBusConnectionString(serviceBus.Port),
            ["ConnectionStrings__messaging-management"] = Container.ServiceBusConnectionString(serviceBus.ManagementPort!.Value),
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        };
        var invoice = Guid.CreateVersion7();
        await SurvivingEvent.PayThenKillTheHostMidDeliveryAsync(
            project.StartHost(new Dictionary<string, string>(dependencies) { [SurvivingEvent.Hangs] = "true" }),
            invoice);

        using var restarted = project.StartHost(dependencies);
        await SurvivingEvent.ShouldBeReceivedAfterTheRestartAsync(restarted, invoice, postgres, DatabasePassword);
    }

    /// <summary>The generated integration tests, and a publish of the AppHost (<see cref="PublishModeTests"/>), pass.</summary>
    [Fact]
    public void The_generated_integration_tests_pass_against_the_running_aspire_stack_with_the_emulator()
    {
        using var project = new GeneratedProject(Profile, Transport);
        project.AddModule("billing");
        project.Write("tests/Integration.Tests/PublishModeTests.cs", PublishModeTests);
        project.Build();
        DockerPorts.EnsureRunsAlone();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        // The project's own integration tests and the billing sample's five (#734).
        output.ShouldContain("succeeded: 16");
    }

    [Fact]
    public async Task An_event_from_the_emulator_whose_consumer_always_fails_is_dead_lettered_in_the_table_the_docs_name()
    {
        // An event the host's own modules publish and consume may be routed to a local queue; this one is sent to the
        // emulator's queue by address, so it is received from the broker, as one another service publishes is (#684).
        using var project = new GeneratedProject(Profile, Transport);
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                public static void Handle(InvoicePaidV1 message) =>
                    throw new InvalidOperationException($"Every delivery of {message.InvoiceId} fails.");
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Api/SendThroughTheBroker.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Contracts;
            using Shop.Contracts.Billing;
            using Wolverine;

            namespace Shop.Modules.Shipping.Api;

            public sealed class SendThroughTheBroker : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapPost("/send-through-the-broker", async (IMessageBus bus) =>
                    {
                        // The metadata another service's unit of work would have filled.
                        var invoicePaid = new InvoicePaidV1(Guid.CreateVersion7()) with
                        {
                            Metadata = new EventMetadata(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, "tenant-a"),
                        };
                        await bus.EndpointFor(new Uri("asb://queue/shop.contracts.billing.invoicepaidv1")).SendAsync(invoicePaid);
                        return Results.Accepted();
                    }).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var serviceBus = Container.ServiceBus();
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["ConnectionStrings__messaging"] = Container.ServiceBusConnectionString(serviceBus.Port),
            ["ConnectionStrings__messaging-management"] = Container.ServiceBusConnectionString(serviceBus.ManagementPort!.Value),
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        var sent = await host.PostAsync("/shipping/send-through-the-broker");
        sent.StatusCode.ShouldBe(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Seven attempts over about a minute (the pack's ADR), then the message is dead-lettered somewhere.
        for (var attempt = 0; attempt < 360 && RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword) == 0; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        // The pack's ADR and agent rules name the table as the one place a dead letter is found and replayed from.
        RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword).ShouldBe(1, $"Queues: {serviceBus.ServiceBusQueues()}");
    }
}

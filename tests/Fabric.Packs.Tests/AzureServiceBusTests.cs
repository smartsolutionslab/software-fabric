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
        versions.ShouldContain("<PackageVersion Include=\"Aspire.Hosting.Azure.ServiceBus\" Version=\"13.6.4\" />");
        versions.ShouldContain("<PackageVersion Include=\"WolverineFx.AzureServiceBus\" Version=\"6.59.1\" />");

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

        // Without a transport Wolverine would deliver the event in process; it was routed to the queue named after it.
        serviceBus.ServiceBusQueues().ShouldContain("<title type=\"text\">shop.contracts.billing.invoicepaidv1</title>");
    }

    [Fact]
    public void The_generated_integration_tests_pass_against_the_running_aspire_stack_with_the_emulator()
    {
        using var project = new GeneratedProject(Profile, Transport);
        project.AddModule("billing");
        project.Build();
        DockerPorts.EnsureRunsAlone();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }
}

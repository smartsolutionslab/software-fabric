using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017) with <c>backend.bus.transport: rabbitmq</c> or <c>azure-service-bus</c>
/// (#762): each service consumes from a queue (RabbitMQ) or subscription (Azure Service Bus) of its own, named after its
/// module and bound to the event's exchange or topic, so one published event reaches every service that consumes it.
/// Services that listened on one queue named after the event would compete for it, and each event would reach only one.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ServicesBrokerTests
{
    private const string Profile = "saas-standard-react";

    private static string[] Services(string transport) => ["backend.topology=services", "backend.tenancy=none", "backend.authorization=roles", $"backend.bus.transport={transport}"];

    /// <summary>An endpoint that publishes InvoicePaidV1 through the billing module's unit of work.</summary>
    private const string PayInvoice = """
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
        """;

    /// <summary>Runs inside the generated integration tests, through the AppHost: billing publishes, shipping and returns both consume.</summary>
    private const string FanOut = """
        using System.Net;

        namespace Shop.Integration.Tests;

        public class FanOutTests(AspireFixture aspire)
        {
            [Fact]
            public async Task One_published_event_reaches_every_service_that_consumes_it()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = aspire.CreateHostClient();
                var invoice = Guid.CreateVersion7();

                using var paid = await client.PostAsync(new Uri($"/billing/invoices/{invoice}/pay", UriKind.Relative), null, cancellationToken);
                paid.StatusCode.ShouldBe(HttpStatusCode.Accepted);

                var received = string.Empty;
                for (var attempt = 0; attempt < 240 && received != "shipping 1, returns 1"; attempt++)
                {
                    await Task.Delay(500, cancellationToken);
                    var shipping = await client.GetStringAsync(new Uri("/shipping/received", UriKind.Relative), cancellationToken);
                    var returns = await client.GetStringAsync(new Uri("/returns/received", UriKind.Relative), cancellationToken);
                    received = $"shipping {shipping}, returns {returns}";
                }

                received.ShouldBe("shipping 1, returns 1", "one event published once reaches both services that consume it.");
            }
        }
        """;

    [Theory]
    [InlineData("rabbitmq", "Messaging__Queue")]
    [InlineData("azure-service-bus", "Messaging__Subscription")]
    public void With_services_the_AppHost_names_each_services_queue_or_subscription_after_its_module(string transport, string variable)
    {
        var files = WithModules(Services(transport), "billing", "shipping");
        var appHost = files["src/AppHost/AppHost.cs"];

        var service = appHost.IndexOf("var service = builder.AddBuiltProject(module, project)", StringComparison.Ordinal);
        service.ShouldBeGreaterThanOrEqualTo(0, appHost);
        var serviceWiring = appHost[service..appHost.IndexOf("host.WithReference(service)", StringComparison.Ordinal)];
        serviceWiring.ShouldContain($".WithEnvironment(\"{variable}\", module)", customMessage: "the loop names every service's queue or subscription after its module.");
        serviceWiring.ShouldContain(".WithReference(messaging)");
        appHost.ShouldNotContain("billing", Case.Insensitive);
        appHost.ShouldNotContain("shipping", Case.Insensitive);
    }

    [Theory]
    [InlineData("rabbitmq", "Messaging:Queue")]
    [InlineData("azure-service-bus", "Messaging:Subscription")]
    public void With_services_every_architecture_rule_that_builds_a_service_host_gives_it_the_name_the_AppHost_would(string transport, string setting)
    {
        var files = WithModules(Services(transport), "billing", "shipping");

        var rules = files.Where(file => file.Key.StartsWith("tests/Architecture.Tests/Rules/", StringComparison.Ordinal)
            && file.Value.Contains("UseSetting(\"ConnectionStrings:messaging\"", StringComparison.Ordinal)).ToList();
        rules.ShouldNotBeEmpty();
        foreach (var (path, content) in rules)
        {
            content.ShouldContain($"builder.UseSetting(\"{setting}\", \"unused\");", customMessage: $"{path} builds a service host, which fails without {setting}.");
        }
    }

    [Fact]
    public void With_services_and_rabbitmq_a_service_listens_on_the_queue_the_AppHost_names_and_fails_without_it()
    {
        var messaging = WithModules(Services("rabbitmq"), "billing")["src/Host/Wiring/Messaging.cs"];

        messaging.ShouldContain("configuration[\"Messaging:Queue\"]");
        messaging.ShouldContain("Messaging:Queue is missing");
        messaging.ShouldContain(".QueueNameForListener(_ => queue)", customMessage: "services that listened on the queue named after the event would compete for it.");
    }

    [Fact]
    public void With_services_and_azure_service_bus_a_service_subscribes_to_the_events_topic_under_the_name_the_AppHost_gives()
    {
        var messaging = WithModules(Services("azure-service-bus"), "billing")["src/Host/Wiring/Messaging.cs"];

        messaging.ShouldContain("configuration[\"Messaging:Subscription\"]");
        messaging.ShouldContain("Messaging:Subscription is missing");
        messaging.ShouldContain(".UseTopicAndSubscriptionConventionalRouting(", customMessage: "services that listened on the queue named after the event would compete for it.");
        messaging.ShouldContain(".SubscriptionNameForListener(_ => subscription)");
        messaging.ShouldNotContain(".UseConventionalRouting(");
    }

    [Theory]
    [InlineData("rabbitmq")]
    [InlineData("azure-service-bus")]
    public void A_modular_monolith_keeps_the_queues_named_after_the_events(string transport)
    {
        var files = WithModules(["backend.tenancy=none", "backend.authorization=roles", $"backend.bus.transport={transport}"], "billing");

        files["src/Host/Wiring/Messaging.cs"].ShouldContain(".UseConventionalRouting(");
        files["src/Host/Wiring/Messaging.cs"].ShouldNotContain("Messaging:");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("Messaging__");
    }

    [Theory]
    [InlineData("rabbitmq")]
    [InlineData("azure-service-bus")]
    public void One_event_published_over_the_broker_reaches_every_service_that_consumes_it_under_the_AppHost(string transport)
    {
        using var project = new GeneratedProject(Profile, Services(transport));
        project.AddModule("billing");
        project.AddModule("shipping");
        project.AddModule("returns");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", PayInvoice);
        foreach (var module in new[] { "Shipping", "Returns" })
        {
            project.Write($"src/Modules/{module}/{module}.Application/InvoicePaidConsumer.cs", Consumer(module));
            project.Write($"src/Modules/{module}/{module}.Api/Received.cs", Received(module));
        }

        project.Write("tests/Integration.Tests/FanOutTests.cs", FanOut);
        project.Build();

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-class", "Shop.Integration.Tests.FanOutTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 1");
    }

    /// <summary>A consumer of InvoicePaidV1 in the given module that counts what it received.</summary>
    private static string Consumer(string module) => $$"""
        using System.Collections.Concurrent;
        using Shop.Contracts.Billing;

        namespace Shop.Modules.{{module}}.Application;

        public sealed class InvoicePaidConsumer
        {
            public static ConcurrentBag<Guid> Received { get; } = [];

            public static void Handle(InvoicePaidV1 message) => Received.Add(message.InvoiceId);
        }
        """;

    /// <summary>An endpoint reading back how many events the module's consumer received.</summary>
    private static string Received(string module) => $$"""
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.Modules.{{module}}.Application;

        namespace Shop.Modules.{{module}}.Api;

        public sealed class Received : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/received", () => InvoicePaidConsumer.Received.Count).AllowAnonymous();
        }
        """;

    private static Dictionary<string, string> WithModules(IReadOnlyList<string> overrides, params string[] modules)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
            var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, module, []);
            addition.Errors.ShouldBeEmpty();
            foreach (var file in addition.Created)
            {
                files.Add(file.Path, file.Content);
            }

            files[FabricFile.FileName] = addition.FabricYaml!;
        }

        return files;
    }
}

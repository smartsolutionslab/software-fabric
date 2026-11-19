using Fabric.Core.Catalog;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> with a realtime option (#767, part of #707; ADR-0017 as amended 2026-11-18): a
/// service publishes a browser event, <c>BrowserEventV1</c>, on the project's broker, and the edge, the one process the
/// browsers are connected to, listens there without a database. The edge is on the broker only when a realtime option
/// is chosen. Realtime still conflicts with services until #768 moves the hub and the stream to the edge, so these tests
/// generate against the catalog with those two conflicts lifted in memory; the catalog keeps them
/// (<see cref="ServicesUnsupportedOptionsTests"/>).
/// </summary>
public sealed class EdgeMessagingTests
{
    private const string Profile = "saas-standard-react";

    private const string EdgeMessaging = "src/Edge/Wiring/EdgeMessaging.cs";

    private const string BrowserEvent = "src/BuildingBlocks/Realtime/BrowserEventV1.cs";

    private const string Message = "Shop.BuildingBlocks.Realtime.BrowserEventV1";

    private static readonly CatalogModel RealtimeOnServices = LiftRealtimeConflicts();

    public static TheoryData<string, string> TransportsAndRealtime { get; } = new()
    {
        { "rabbitmq", "signalr" },
        { "rabbitmq", "sse" },
        { "azure-service-bus", "signalr" },
        { "azure-service-bus", "sse" },
        { "kafka", "signalr" },
        { "kafka", "sse" },
    };

    public static TheoryData<string> Transports { get; } = ["rabbitmq", "azure-service-bus", "kafka"];

    public static TheoryData<string> Realtime { get; } = ["signalr", "sse"];

    [Theory]
    [MemberData(nameof(TransportsAndRealtime))]
    public void With_services_and_realtime_the_edge_connects_to_the_broker_without_a_database(string transport, string realtime)
    {
        var files = Generate(RealtimeOnServices, Services(transport, realtime));

        files.Keys.ShouldContain(EdgeMessaging);
        var messaging = files[EdgeMessaging];
        messaging.ShouldContain("public static IServiceCollection AddEdgeMessaging(this IServiceCollection services, IConfiguration configuration)");
        messaging.ShouldContain("""configuration.GetConnectionString("messaging")""");
        messaging.ShouldNotContain("\"database\"", customMessage: "the edge has no database.");
        messaging.ShouldNotContain("PersistMessagesWith", customMessage: "the edge stores no message: a browser event is not durable.");
        messaging.ShouldNotContain("UseDurable", customMessage: "the edge stores no message: a browser event is not durable.");
        messaging.ShouldNotContain("EntityFrameworkCore");
        messaging.ShouldContain(transport switch
        {
            "rabbitmq" => "options.UseRabbitMq(new Uri(broker))",
            "azure-service-bus" => "options.UseAzureServiceBus(broker)",
            _ => "options.UseKafka(broker)",
        });

        files["src/Edge/Program.cs"].ShouldContain("builder.Services.AddEdgeMessaging(builder.Configuration);", customMessage: "the Wolverine pack's edge.services contribution");
        var service = files["src/Services/Billing/Host/Program.cs"];
        service.ShouldNotContain("AddEdgeMessaging");
        service.ShouldContain("builder.Services.AddMessaging(builder.Configuration, modules);");
    }

    [Theory]
    [MemberData(nameof(TransportsAndRealtime))]
    public void The_edge_listens_for_what_its_own_consumers_handle_on_the_exchange_or_topic_named_after_the_message(string transport, string realtime)
    {
        var messaging = Generate(RealtimeOnServices, Services(transport, realtime))[EdgeMessaging];

        messaging.ShouldContain("options.Discovery.DisableConventionalDiscovery();");
        messaging.ShouldContain("""options.Discovery.CustomizeHandlerDiscovery(query => query.Includes.WithNameSuffix("Consumer"));""");
        messaging.ShouldContain("options.Discovery.IncludeAssembly(typeof(EdgeMessaging).Assembly);", customMessage: "the edge runs no module code: only its own consumers.");
        messaging.ShouldNotContain("IModule", customMessage: "the edge runs no module code.");
        messaging.ShouldContain(transport switch
        {
            "rabbitmq" => "rabbitMq.BindExchange(consumed.FullName!).ToQueue(queue);",
            "azure-service-bus" => "options.ListenToAzureServiceBusSubscription(subscription).FromTopic(consumed.FullName!);",
            _ => "options.ListenToKafkaTopic(consumed.FullName!);",
        });
        messaging.ShouldContain(transport switch
        {
            "rabbitmq" => """configuration["Messaging:Queue"]""",
            "azure-service-bus" => """configuration["Messaging:Subscription"]""",
            _ => """configuration["Messaging:ConsumerGroup"]""",
        });
    }

    [Theory]
    [MemberData(nameof(TransportsAndRealtime))]
    public void The_AppHost_hands_the_edge_the_broker_under_the_edge_s_own_name(string transport, string realtime)
    {
        var appHost = Generate(RealtimeOnServices, Services(transport, realtime))["src/AppHost/AppHost.cs"];

        var edge = appHost[appHost.IndexOf("""var host = builder.AddProject("host", "../Edge/Edge.csproj")""", StringComparison.Ordinal)..appHost.IndexOf("foreach (var (module, project) in builder.ServiceHosts())", StringComparison.Ordinal)];
        edge.ShouldContain(".WithReference(messaging)");
        edge.ShouldContain(".WaitFor(messaging)");
        edge.ShouldContain(transport switch
        {
            "rabbitmq" => """.WithEnvironment("Messaging__Queue", "host")""",
            "azure-service-bus" => """.WithEnvironment("Messaging__Subscription", "host")""",
            _ => """.WithEnvironment("Messaging__ConsumerGroup", "host")""",
        });
        if (transport == "azure-service-bus")
        {
            edge.ShouldContain("ConnectionStrings__messaging-management");
        }

        edge.TrimEnd().ShouldEndWith(";", customMessage: "the edge's wiring is one chained statement.");
    }

    [Theory]
    [MemberData(nameof(TransportsAndRealtime))]
    public void A_service_routes_BrowserEventV1_to_the_broker_without_storing_it(string transport, string realtime)
    {
        var files = Generate(RealtimeOnServices, Services(transport, realtime));

        var browserEvent = files[BrowserEvent];
        browserEvent.ShouldContain("namespace Shop.BuildingBlocks.Realtime;");
        browserEvent.ShouldContain("public sealed record BrowserEventV1(string Type, JsonElement Payload);");
        browserEvent.ShouldNotContain("IntegrationEvent", customMessage: "a browser event is not an integration event.");

        var messaging = files["src/Host/Wiring/Messaging.cs"];
        messaging.ShouldContain($"options.PublishMessage<{Message}>()");
        messaging.ShouldContain(transport switch
        {
            "rabbitmq" => $".ToRabbitExchange(typeof({Message}).FullName!)",
            "azure-service-bus" => $".ToAzureServiceBusTopic(typeof({Message}).FullName!)",
            _ => $".ToKafkaTopic(typeof({Message}).FullName!)",
        });
        messaging.ShouldContain(".BufferedInMemory();", customMessage: "a browser event is not durable: the outbox policy does not store it.");
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public void With_the_Wolverine_mediator_the_route_is_rendered_too(string transport)
    {
        var messaging = Generate(RealtimeOnServices, [.. Services(transport, "signalr"), "backend.mediator=wolverine"])["src/Host/Wiring/Messaging.cs"];

        messaging.ShouldContain($"options.PublishMessage<{Message}>()", customMessage: "the mediator's conventional routing sends integration events only.");
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public void With_services_and_no_realtime_the_edge_is_not_on_the_broker(string transport)
    {
        var files = Generate(Repository.Catalog, ["backend.topology=services", "backend.tenancy=none", $"backend.bus.transport={transport}"]);

        files.Keys.ShouldNotContain(EdgeMessaging);
        files.Keys.ShouldNotContain(BrowserEvent);
        files["src/Edge/Program.cs"].ShouldNotContain("Messaging");
        files["src/Host/Wiring/Messaging.cs"].ShouldNotContain("BrowserEventV1");
        var appHost = files["src/AppHost/AppHost.cs"];
        appHost[..appHost.IndexOf("foreach (var (module, project) in builder.ServiceHosts())", StringComparison.Ordinal)]
            .ShouldNotContain("WithReference(messaging)", customMessage: "the edge is on the broker only with a realtime option.");
    }

    [Theory]
    [MemberData(nameof(Realtime))]
    public void A_modular_monolith_with_realtime_sends_no_browser_event_over_the_broker(string realtime)
    {
        var files = Generate(Repository.Catalog, ["backend.bus.transport=rabbitmq", $"frontend.realtime={realtime}"]);

        files.Keys.ShouldNotContain(BrowserEvent);
        files.Keys.ShouldNotContain(path => path.StartsWith("src/Edge/", StringComparison.Ordinal));
        files["src/Host/Wiring/Messaging.cs"].ShouldNotContain("BrowserEventV1");
    }

    private static string[] Services(string transport, string realtime) =>
        ["backend.topology=services", "backend.tenancy=none", $"backend.bus.transport={transport}", $"frontend.realtime={realtime}"];

    private static Dictionary<string, string> Generate(CatalogModel catalog, string[] overrides)
    {
        var generated = Generator.New(catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
        var addition = Generator.AddModule(catalog, Repository.Packs.Packs, record, "billing", []);
        addition.Errors.ShouldBeEmpty();
        foreach (var file in addition.Created)
        {
            files.Add(file.Path, file.Content);
        }

        return files;
    }

    /// <summary>The catalog with realtime's two conflicts with services removed, which #768 removes from the catalog itself.</summary>
    private static CatalogModel LiftRealtimeConflicts()
    {
        const string Conflict = """, conflicts: ["backend.topology == services"] }""";
        var text = File.ReadAllText(Path.Combine(Repository.Root, "catalog", "catalog.yaml"));
        foreach (var option in new[] { "signalr: { tier: experimental", "sse:     { tier: experimental" })
        {
            text.ShouldContain(option + Conflict, customMessage: "#768 lifted the conflict: generate against Repository.Catalog and drop this helper.");
            text = text.Replace(option + Conflict, option + " }", StringComparison.Ordinal);
        }

        return CatalogReader.Read(new StringReader(text), "catalog.yaml");
    }
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.realtime: signalr | sse</c> with <c>backend.topology: services</c> (ADR-0017 as amended 2026-11-18,
/// #768): the hub or the stream, and the edge's consumer of <c>BrowserEventV1</c>, live on the edge, the one process
/// the browsers are connected to. A service's <c>IBrowserEvents</c> publishes <c>BrowserEventV1</c> on the bus, and
/// each service maps the Development-only test endpoint under its own module, so the generated tests prove
/// service → bus → edge → browser. V1 runs one edge.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class ServicesServerEventsTests
{
    private const string Profile = "saas-standard-react";

    public static TheoryData<string> Realtime { get; } = ["signalr", "sse"];

    [Theory]
    [MemberData(nameof(Realtime))]
    public void With_services_realtime_is_no_longer_refused(string realtime)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, Services(realtime));

        generated.Errors.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Realtime))]
    public void With_services_the_hub_or_the_stream_and_the_consumer_of_BrowserEventV1_live_on_the_edge(string realtime)
    {
        var files = WithModules(Services(realtime), "billing");

        var edge = files["src/Edge/Program.cs"];
        edge.ShouldContain("builder.Services.AddServerEvents();");
        edge.ShouldContain("app.MapServerEvents();");

        var registration = files["src/Edge/Wiring/ServerEventsRegistration.cs"];
        registration.ShouldContain(realtime == "signalr" ? "services.AddSignalR();" : "services.AddSingleton<BrowserEventStreams>();");
        registration.ShouldContain("access_token", customMessage: "a browser cannot send a header on a WebSocket or an EventSource.");
        registration.ShouldNotContain("IBrowserEvents", customMessage: "the edge runs no module code: it pushes what the services publish.");

        var endpoints = files["src/Edge/Wiring/ServerEventsEndpoints.cs"];
        endpoints.ShouldContain(realtime == "signalr" ? """app.MapHub<EventsHub>("/hubs/events",""" : """app.MapGet("/server-events",""");
        endpoints.ShouldNotContain("/test", customMessage: "the test endpoint is each service's own.");

        files.Keys.ShouldContain(realtime == "signalr" ? "src/Edge/Realtime/EventsHub.cs" : "src/Edge/Realtime/BrowserEventStreams.cs");
        var consumer = files["src/Edge/Realtime/BrowserEventConsumer.cs"];
        consumer.ShouldContain("namespace Shop.Edge.Realtime;");
        consumer.ShouldContain("public sealed class BrowserEventConsumer(");
        consumer.ShouldContain("BrowserEventV1 message");
        consumer.ShouldContain(realtime == "signalr"
            ? "hub.Clients.All.SendAsync(message.Type, message.Payload, cancellationToken)"
            : "streams.Send(message.Type, message.Payload);");

        foreach (var path in new[]
        {
            "src/Host/Realtime/EventsHub.cs",
            "src/Host/Realtime/HubBrowserEvents.cs",
            "src/Host/Realtime/BrowserEventStreams.cs",
            "src/Host/Realtime/StreamBrowserEvents.cs",
            "src/Host/Wiring/ServerEventsRegistration.cs",
            "src/Host/Wiring/ServerEventsEndpoints.cs",
        })
        {
            files.Keys.ShouldNotContain(path, customMessage: "every service host compiles src/Host: the hub or the stream would be in each of them.");
        }
    }

    [Theory]
    [MemberData(nameof(Realtime))]
    public void With_services_a_services_IBrowserEvents_publishes_BrowserEventV1_on_the_bus(string realtime)
    {
        var files = WithModules(Services(realtime), "billing");

        var sender = files["src/Host/Realtime/BusBrowserEvents.cs"];
        sender.ShouldContain("public sealed class BusBrowserEvents(IMessageBus bus) : IBrowserEvents");
        sender.ShouldContain("new BrowserEventV1(typeof(TEvent).Name, JsonSerializer.SerializeToElement(browserEvent, JsonSerializerOptions.Web))");
        sender.ShouldContain("bus.PublishAsync(");
        files["src/Host/Wiring/BrowserEventsRegistration.cs"].ShouldContain("services.AddScoped<IBrowserEvents, BusBrowserEvents>()");

        var service = files["src/Services/Billing/Host/Program.cs"];
        service.ShouldContain("builder.Services.AddBrowserEvents();");
        service.ShouldContain("app.MapBrowserEventsTest();");
        service.ShouldNotContain("AddServerEvents", customMessage: "a service has no hub and no stream.");
        service.ShouldNotContain("MapServerEvents", customMessage: "a service has no hub and no stream.");
        files["src/BuildingBlocks/Realtime/IBrowserEvents.cs"].ShouldContain("src/Edge");
    }

    [Theory]
    [MemberData(nameof(Realtime))]
    public void With_services_the_development_test_endpoint_is_each_services_own_and_the_tests_send_through_it(string realtime)
    {
        var files = WithModules(Services(realtime), "billing", "order-items");

        var endpoint = files["src/Host/Wiring/BrowserEventsTestEndpoint.cs"];
        endpoint.ShouldContain("if (app.Environment.IsDevelopment())");
        endpoint.ShouldContain("""app.MapPost("/" + ServiceModule.Name + "/server-events/test",""");
        endpoint.ShouldContain(".RequireAuthorization()");
        endpoint.ShouldContain(".ExcludeFromDescription();");
        endpoint.ShouldNotContain("billing", Case.Insensitive);

        var integration = files["tests/Integration.Tests/ServerEventsTests.cs"];
        integration.ShouldContain("public async Task A_test_event_from_every_service_reaches_a_browser_connected_to_the_edge()");
        integration.ShouldContain("$\"/{module}/server-events/test\"");
        integration.ShouldContain("GetRequiredService<DistributedApplicationModel>()", customMessage: "the services are the AppHost's own, so a new module's is sent from too.");
        integration.ShouldNotContain("new Uri(\"/hubs/events/test\"");
        integration.ShouldNotContain("new Uri(\"/server-events/test\"");

        files.Keys.ShouldNotContain("web/app/e2e/server-events.spec.ts");
        files["web/app/e2e/billing-server-events.spec.ts"].ShouldContain("`/api/billing/server-events/test`");
        files["web/app/e2e/order-items-server-events.spec.ts"].ShouldContain("`/api/order-items/server-events/test`");
    }

    /// <summary>
    /// With services the spec's title names the module, so a long name took the <c>test(...)</c> line past the
    /// printWidth of 120 and <c>pnpm format:check</c> failed (#814). Both auth branches: they word the title differently.
    /// The longest name also takes the test endpoint's URL past 120.
    /// </summary>
    [Theory]
    [InlineData("signalr", "keycloak")]
    [InlineData("signalr", "none")]
    [InlineData("sse", "keycloak")]
    [InlineData("sse", "none")]
    public void With_services_every_services_end_to_end_spec_passes_the_format_check(string realtime, string auth)
    {
        using var project = new GeneratedProject(Profile, [.. Services(realtime), $"backend.auth={auth}"]);
        foreach (var module in new[] { "billing", "dashboards", "assistant", "warehouse-notifications" })
        {
            project.AddModule(module);
        }

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);

        var specs = Directory.GetFiles(Path.Combine(web, "app", "e2e"), "*-server-events.spec.ts")
            .Select(path => "e2e/" + Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        specs.ShouldBe(
        [
            "e2e/assistant-server-events.spec.ts",
            "e2e/billing-server-events.spec.ts",
            "e2e/dashboards-server-events.spec.ts",
            "e2e/warehouse-notifications-server-events.spec.ts",
        ]);

        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), "pnpm", ["exec", "prettier", "--check", .. specs]);
        exitCode.ShouldBe(0, output);
    }

    [Theory]
    [MemberData(nameof(Realtime))]
    public void A_modular_monolith_keeps_its_hub_or_stream_and_its_test_endpoint_in_src_Host(string realtime)
    {
        var files = WithModules(["backend.tenancy=none", "backend.bus.transport=rabbitmq", $"frontend.realtime={realtime}"], "billing");

        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddServerEvents();");
        files["src/Host/Program.cs"].ShouldContain("app.MapServerEvents();");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain(realtime == "signalr" ? "\"/hubs/events/test\"" : "\"/server-events/test\"");
        files.Keys.ShouldContain(realtime == "signalr" ? "src/Host/Realtime/HubBrowserEvents.cs" : "src/Host/Realtime/StreamBrowserEvents.cs");
        files.Keys.ShouldNotContain("src/Host/Realtime/BusBrowserEvents.cs");
        files.Keys.ShouldNotContain("src/Host/Wiring/BrowserEventsTestEndpoint.cs");
        files.Keys.ShouldNotContain(path => path.StartsWith("src/Edge/", StringComparison.Ordinal));
        files.Keys.ShouldContain("web/app/e2e/server-events.spec.ts");
        files.Keys.ShouldNotContain("web/app/e2e/billing-server-events.spec.ts");
    }

    [Theory]
    [MemberData(nameof(Realtime))]
    public void Its_rules_and_adr_say_the_hub_or_stream_is_on_the_edge_with_one_edge_replica_in_v1(string realtime)
    {
        var files = WithModules(Services(realtime), "billing");

        files["AGENTS.md"].ShouldContain("BrowserEventV1");
        files["AGENTS.md"].ShouldContain("POST /<module>/server-events/test");
        files["AGENTS.md"].ShouldContain("one edge replica");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith($"-frontend-realtime-{realtime}.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("src/Edge");
        adr.ShouldContain("BrowserEventV1");
        adr.ShouldContain("one edge replica");
        var topology = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-backend-topology-services.md", StringComparison.Ordinal)).Value;
        topology.ShouldContain("one edge replica");
    }

    [Theory]
    [MemberData(nameof(Realtime))]
    public void A_service_publishes_and_a_browser_connected_to_the_edge_receives_it_and_the_architecture_tests_pass(string realtime)
    {
        using var project = new GeneratedProject(Profile, Services(realtime));
        project.AddModule("billing");
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0", customMessage: "SingletonsLiveOnTheEdge: no service host maps the hub or the stream.");

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-class", "Shop.Integration.Tests.ServerEventsTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0", customMessage: output);
        Succeeded().IsMatch(output).ShouldBeTrue(output);
    }

    private static string[] Services(string realtime) =>
        ["backend.topology=services", "backend.tenancy=none", "backend.bus.transport=rabbitmq", $"frontend.realtime={realtime}"];

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

    [GeneratedRegex(@"succeeded: [3-9]")]
    private static partial Regex Succeeded();
}

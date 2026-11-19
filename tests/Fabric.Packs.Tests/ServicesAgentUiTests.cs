using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.agent-ui: ag-ui</c> with <c>backend.topology: services</c> (ADR-0013 and ADR-0017, amended 2026-11-18,
/// #769): the service that owns an agent speaks AG-UI at <c>POST /{module}/agents/{name}</c> and lists its agents'
/// names at <c>GET /{module}/agents</c>. The edge reads every service's list once at startup, with a
/// client-credentials client of its own (the owner's Q2 on #707), builds one route per agent and forwards
/// <c>POST /agents/{name}</c> to the owning service; it refuses to start on a name two services share and answers an
/// unknown one with 404. The edge never speaks AG-UI and runs no agent.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class ServicesAgentUiTests
{
    private const string Profile = "saas-standard-react";
    private const string Rule = "tests/Architecture.Tests/Rules/AgentUiGoesThroughAQuery.cs";

    private static readonly string[] Agents = ["backend.llm=anthropic", "backend.llm.agents=agent-framework", "frontend.agent-ui=ag-ui"];

    public static TheoryData<string> Providers { get; } = ["keycloak", "generic-oidc", "entra-id"];

    [Fact]
    public void With_services_ag_ui_is_no_longer_refused()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, Services("keycloak"));

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void With_services_the_owning_service_speaks_ag_ui_under_its_module_and_lists_its_agents_for_the_edge()
    {
        var files = WithModules(Services("keycloak"), "billing");

        var endpoints = files["src/Host/Wiring/AgentUiEndpoints.cs"];
        endpoints.ShouldContain("""app.MapPost("/" + ServiceModule.Name + "/agents/{name}", AgentRuns.RunAsync)""");
        endpoints.ShouldContain("""app.MapGet("/" + ServiceModule.Name + "/agents", (AgentConversations conversations) => conversations.Names)""");
        endpoints.ShouldNotContain("""app.MapPost("/agents/{name}",""", customMessage: "/agents at a service's root is the edge's (SingletonsLiveOnTheEdge).");
        Regex.Count(endpoints, @"\.RequireAuthorization\(\)").ShouldBe(2, endpoints);
        Regex.Count(endpoints, @"\.ExcludeFromDescription\(\)").ShouldBe(2, customMessage: "neither is part of the OpenAPI document the frontend's client is generated from.");
        endpoints.ShouldNotContain("billing", Case.Insensitive);
        files["src/Host/Agents/AgentConversations.cs"].ShouldContain("public IReadOnlyList<string> Names");

        foreach (var module in new[] { "Assistant", "Billing" })
        {
            files[$"src/Services/{module}/Host/Program.cs"].ShouldContain("builder.Services.AddAgentUi(builder.Configuration, builder.Environment);");
            files[$"src/Services/{module}/Host/Program.cs"].ShouldContain("app.MapAgentUi();");
        }
    }

    [Fact]
    public void With_services_the_edge_reads_the_agent_table_at_startup_and_forwards_each_agent_to_its_service()
    {
        var files = WithModules(Services("keycloak"), "billing");

        var edge = files["src/Edge/Program.cs"];
        edge.ShouldContain("builder.Services.AddAgentRoutes(builder.Configuration);");
        edge.ShouldContain("app.MapAgentRoutes();");
        edge.ShouldNotContain("AddAgentUi", customMessage: "the edge runs no agent.");
        edge.ShouldNotContain("MapAgentUi", customMessage: "the edge never speaks AG-UI.");

        var routes = files["src/Edge/Agents/AgentRoutes.cs"];
        routes.ShouldContain("IHostedLifecycleService", customMessage: "the table is read before the edge starts listening.");
        routes.ShouldContain("ModuleRoutes.Modules(configuration)", customMessage: "the services are the ones the AppHost started, by the same convention as the module routes.");
        routes.ShouldContain("\"/agents\"");
        routes.ShouldContain("throw new InvalidOperationException(", customMessage: "two services naming one agent stop the edge.");

        var table = files["src/Edge/Agents/AgentRouteTable.cs"];
        table.ShouldContain("Problems");
        table.ShouldContain("both name the agent");
        table.ShouldContain("Path = \"/agents/\" + agent");
        table.ShouldContain("[\"PathPrefix\"] = \"/\" + module");
        table.ShouldContain("Methods = [\"POST\"]");

        var notFound = files["src/Edge/Wiring/AgentRouteEndpoints.cs"];
        notFound.ShouldContain("""app.MapPost("/agents/{name}",""");
        notFound.ShouldContain("agents.not-found");
        notFound.ShouldContain(".RequireAuthorization()");
        notFound.ShouldContain(".ExcludeFromDescription()");

        // The edge's own code; Edge.csproj references the AG-UI SDK because the edge compiles src/Host as every service host does.
        foreach (var (path, content) in files.Where(file => file.Key.StartsWith("src/Edge/", StringComparison.Ordinal) && file.Key.EndsWith(".cs", StringComparison.Ordinal)))
        {
            content.ShouldNotContain("AGUI", customMessage: $"{path}: the edge never speaks AG-UI.");
            content.ShouldNotContain("AgentConversations", customMessage: $"{path}: the edge runs no agent.");
            content.ShouldNotContain("billing", Case.Insensitive, $"{path}: the edge has no line per module.");
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void With_services_the_edge_reads_the_table_with_a_client_credentials_client_of_its_own(string provider)
    {
        var files = WithModules(Services(provider), "billing");

        var token = files["src/Edge/Agents/EdgeClientToken.cs"];
        token.ShouldContain("[\"grant_type\"] = \"client_credentials\"");
        token.ShouldContain("EdgeClient:TokenEndpoint");
        token.ShouldContain("EdgeClient:ClientId");
        token.ShouldContain("EdgeClient:ClientSecret");
        token.ShouldContain("new AuthenticationHeaderValue(\"Bearer\",");

        var registration = files["src/Edge/Wiring/AgentRoutesRegistration.cs"];
        registration.ShouldContain(".RetryEveryMethod()");
        registration.ShouldContain("a second token supersedes the first", customMessage: "a token mint opts back into retry with its reason at the call site.");
        registration.ShouldContain(".AddHttpMessageHandler<EdgeClientToken>()");

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("\"edge-client-secret\"", customMessage: "the client's secret is an Aspire secret parameter, never in the repository.");
        appHost.ShouldContain("EdgeClient__TokenEndpoint");
        appHost.ShouldContain("EdgeClient__ClientId");
        appHost.ShouldContain("EdgeClient__ClientSecret");
        appHost.ShouldContain("shop-edge");

        if (provider == "entra-id")
        {
            files["src/IdentityStandIn/StandInEndpoints.cs"].ShouldContain("case \"client_credentials\":");
            files["src/IdentityStandIn/StandInSettings.cs"].ShouldContain("Applications");
            appHost.ShouldContain("EdgeClient__Scope");
        }
        else
        {
            var realm = files["src/AppHost/realms/realm.json"];
            realm.ShouldContain("\"clientId\": \"shop-edge\"");
            realm.ShouldContain("\"serviceAccountsEnabled\": true");
            realm.ShouldContain("\"secret\": \"${EDGE_CLIENT_SECRET}\"", customMessage: "the realm reads the secret the AppHost generates; none is committed.");
        }

        foreach (var (path, content) in files.Where(file => file.Key.StartsWith("src/Edge/", StringComparison.Ordinal)))
        {
            content.ShouldNotContain("/realms/", customMessage: $"{path}: the edge knows its token endpoint from configuration only.");
        }
    }

    [Fact]
    public void Without_authentication_the_edge_reads_the_table_without_a_token()
    {
        var files = WithModules(["backend.topology=services", "backend.tenancy=none", "backend.auth=none", .. Agents], "billing");

        files.Keys.ShouldNotContain("src/Edge/Agents/EdgeClientToken.cs");
        files["src/Edge/Wiring/AgentRoutesRegistration.cs"].ShouldNotContain("EdgeClientToken");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("edge-client-secret");
        files["src/Host/Wiring/AgentUiEndpoints.cs"].ShouldNotContain(".RequireAuthorization()");
    }

    [Fact]
    public void With_services_the_rule_checks_every_service_host_and_the_edge()
    {
        var files = WithModules(Services("keycloak"), "billing");

        var rule = files[Rule];
        rule.ShouldContain("ServiceHostEntryPoints.All");
        rule.ShouldNotContain("typeof(Program)", customMessage: "every service host has a Program of its own.");
        rule.ShouldContain("the edge");
        rule.ShouldContain("AgentConversations");
        files["tests/Architecture.Tests/Architecture.Tests.csproj"].ShouldContain("""<ProjectReference Include="../../src/Edge/Edge.csproj" Aliases="Edge" />""");
    }

    [Fact]
    public void A_modular_monolith_keeps_its_one_agent_endpoint_at_the_root_and_no_edge_client()
    {
        var files = WithModules(["backend.tenancy=none", .. Agents], "billing");

        files["src/Host/Wiring/AgentUiEndpoints.cs"].ShouldContain("""app.MapPost("/agents/{name}", AgentRuns.RunAsync)""");
        files["src/Host/Wiring/AgentUiEndpoints.cs"].ShouldNotContain("ServiceModule");
        files["src/Host/Agents/AgentConversations.cs"].ShouldNotContain("public IReadOnlyList<string> Names", Case.Sensitive);
        files[Rule].ShouldContain("typeof(Program).Assembly");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("edge-client-secret");
        files["src/AppHost/realms/realm.json"].ShouldNotContain("shop-edge");
        files.Keys.ShouldNotContain(path => path.StartsWith("src/Edge/", StringComparison.Ordinal));
    }

    [Fact]
    public void Its_rules_and_adr_say_the_owning_service_speaks_ag_ui_and_the_edge_forwards_it()
    {
        var files = WithModules(Services("keycloak"), "billing");

        files["AGENTS.md"].ShouldContain("POST /<module>/agents/{name}");
        files["AGENTS.md"].ShouldContain("client-credentials");
        files["AGENTS.md"].ShouldNotContain("(`/me`, a hub, AG-UI)", customMessage: "AG-UI is spoken by the owning service, not mapped on the edge.");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-agent-ui-ag-ui.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("POST /<module>/agents/{name}");
        adr.ShouldContain("client-credentials");
        adr.ShouldContain("refuses to start");
        var topology = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-backend-topology-services.md", StringComparison.Ordinal)).Value;
        topology.ShouldNotContain("(`/me`, a hub, AG-UI)");
    }

    [Fact]
    public void An_agent_called_through_the_edge_runs_in_its_service_and_the_architecture_tests_pass()
    {
        using var project = new GeneratedProject(Profile, Services("keycloak"));
        project.AddModule("billing");
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0", customMessage: "AgentUiGoesThroughAQuery and SingletonsLiveOnTheEdge hold.");

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-class", "Shop.Integration.Tests.AgentUiTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0", customMessage: output);
        Succeeded().IsMatch(output).ShouldBeTrue(output);
    }

    private static string[] Services(string provider) =>
        ["backend.topology=services", "backend.tenancy=none", $"backend.auth={provider}", .. Agents];

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

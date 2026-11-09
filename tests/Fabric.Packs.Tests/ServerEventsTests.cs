using System.Net;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.realtime.signalr: a hub at /hubs/events in the host, added through the host slots, and the
/// @microsoft/signalr client in the web app. It requires sign-in unless backend.auth is none, and with tenancy an event
/// reaches the browsers of its tenant only.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ServerEventsTests
{
    private const string Profile = "saas-standard-react";
    private const string SignalR = "frontend.realtime=signalr";
    private const string DatabasePassword = "fabric-tests";

    private static readonly string[] WithoutAuthentication = [SignalR, "backend.auth=none"];

    [Fact]
    public void Saas_standard_react_with_signalr_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [SignalR]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_hub_requires_sign_in_and_the_client_sends_the_access_token()
    {
        var files = Files(Profile, SignalR);

        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddServerEvents();");
        files["src/Host/Program.cs"].ShouldContain("app.MapServerEvents();");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain("MapHub<EventsHub>(\"/hubs/events\", options => options.CloseOnAuthenticationExpiration = true)");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain(".RequireAuthorization()");
        files["web/app/package.json"].ShouldContain("\"@microsoft/signalr\": \"10.0.11\"");
        files["web/app/src/realtime/connection.ts"].ShouldContain("accessTokenFactory");
    }

    [Fact]
    public void Without_authentication_the_hub_is_anonymous_and_the_client_sends_no_token()
    {
        var files = Files("lean-crud", WithoutAuthentication);

        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain("MapHub<EventsHub>(\"/hubs/events\")");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldNotContain("RequireAuthorization");
        files["src/Host/Wiring/ServerEventsRegistration.cs"].ShouldNotContain("JwtBearer");
        files["web/app/src/realtime/connection.ts"].ShouldNotContain("accessToken");
    }

    /// <summary>A browser's tenant comes from its user's token, so the catalog refuses tenancy without authentication (#464).</summary>
    [Fact]
    public void Signalr_with_tenancy_and_no_authentication_is_refused_by_the_catalog()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [SignalR, "backend.auth=none"]);

        generated.Errors.ShouldBe(["backend.tenancy=column requires backend.auth != none, but backend.auth is none."]);
    }

    [Fact]
    public void Under_schema_per_tenant_a_browser_joins_its_users_tenant_group_as_under_a_tenant_column()
    {
        // The tenant still comes from the signed-in user's token; only where its rows live differs, which the hub never reads.
        var column = Files(Profile, SignalR);
        var schema = Files(Profile, SignalR, "backend.tenancy=schema-per-tenant");

        schema["src/Host/Realtime/EventsHub.cs"].ShouldNotContain("#error");
        schema["src/Host/Realtime/EventsHub.cs"].ShouldContain("Groups.AddToGroupAsync(Context.ConnectionId, HubBrowserEvents.TenantGroup(tenantId), Context.ConnectionAborted)");
        foreach (var path in (string[])["src/Host/Realtime/EventsHub.cs", "src/Host/Realtime/HubBrowserEvents.cs", "src/BuildingBlocks/Realtime/IBrowserEvents.cs", "src/Host/Wiring/ServerEventsEndpoints.cs", "src/Host/Wiring/ServerEventsRegistration.cs", "tests/Integration.Tests/ServerEventsTests.cs"])
        {
            schema[path].ShouldBe(column[path], path);
        }
    }

    [Fact]
    public void Every_profile_without_signalr_generates_no_server_events()
    {
        foreach (var profile in new[] { "saas-standard-react", "lean-crud", "saas-strict-react" })
        {
            var files = Files(profile);

            files.Keys.ShouldNotContain(path => path.Contains("ServerEvents", StringComparison.Ordinal) || path.Contains("/realtime/", StringComparison.Ordinal), profile);
            files["web/app/vite.config.ts"].ShouldNotContain("ws: true", customMessage: profile);
        }
    }

    [Fact]
    public async Task Without_authentication_an_anonymous_browser_connects_and_the_project_builds_and_passes_its_checks()
    {
        using var project = new GeneratedProject("lean-crud", WithoutAuthentication);
        project.AddModule("billing");
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        using (var postgres = Container.Postgres(DatabasePassword))
        using (var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        }))
        {
            // SignalR's negotiation is where a browser's connection starts; without a token it is answered.
            using var negotiation = await host.PostAsync("/hubs/events/negotiate?negotiateVersion=1");
            negotiation.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await negotiation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("connectionToken");
        }

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

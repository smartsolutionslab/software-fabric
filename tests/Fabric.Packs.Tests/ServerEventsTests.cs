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

    [Fact]
    public void Signalr_with_tenancy_and_no_authentication_fails_the_build_naming_the_combination()
    {
        var files = Files(Profile, SignalR, "backend.auth=none");
        const string Refusal = "#error frontend.realtime signalr with backend.tenancy column needs an identity provider";

        files["src/Host/Realtime/EventsHub.cs"].ShouldStartWith(Refusal);
        files["tests/Integration.Tests/ServerEventsTests.cs"].ShouldStartWith(Refusal);
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

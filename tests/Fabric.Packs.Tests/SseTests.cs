using System.Net;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.realtime.sse (M3-55): a server-sent-events stream at /server-events in the host, added through the host
/// slots, and the browser's own EventSource in the web app. Like signalr's hub, it requires sign-in unless backend.auth
/// is none, and with tenancy an event reaches the browsers of its tenant only. An EventSource cannot send a header, so
/// the browser's access token travels in the query, through the identity-provider vocabulary (docs/slots.md).
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class SseTests
{
    private const string Profile = "saas-standard-react";
    private const string Sse = "frontend.realtime=sse";
    private const string DatabasePassword = "fabric-tests";

    private static readonly string[] WithoutAuthentication = [Sse, "backend.auth=none"];

    [Fact]
    public void Saas_standard_react_with_sse_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Sse]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_stream_requires_sign_in_and_the_client_sends_the_access_token_in_the_query()
    {
        var files = Files(Profile, Sse);

        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddServerEvents();");
        files["src/Host/Program.cs"].ShouldContain("app.MapServerEvents();");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain("app.MapGet(\"/server-events\"");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain(".RequireAuthorization()");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain("TypedResults.ServerSentEvents(");
        files["src/Host/Wiring/ServerEventsRegistration.cs"].ShouldContain("\"/server-events\"");
        files["web/app/src/realtime/connection.ts"].ShouldContain("new EventSource(");
        files["web/app/src/realtime/connection.ts"].ShouldContain("access_token=");
    }

    [Fact]
    public void Sse_brings_no_package_of_its_own()
    {
        var files = Files(Profile, Sse);

        files["web/app/package.json"].ShouldNotContain("signalr");
        files["Directory.Packages.props"].ShouldNotContain("SignalR");
        files["web/app/vite.config.ts"].ShouldNotContain("ws: true");
        files.Keys.ShouldNotContain("src/Host/Realtime/EventsHub.cs");
    }

    [Fact]
    public void Without_authentication_the_stream_is_anonymous_and_the_client_sends_no_token()
    {
        var files = Files("lean-crud", WithoutAuthentication);

        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldContain("app.MapGet(\"/server-events\"");
        files["src/Host/Wiring/ServerEventsEndpoints.cs"].ShouldNotContain("RequireAuthorization");
        files["src/Host/Wiring/ServerEventsRegistration.cs"].ShouldNotContain("JwtBearer");
        files["web/app/src/realtime/connection.ts"].ShouldNotContain("access_token");
        files["web/app/src/realtime/connection.ts"].ShouldNotContain("accessToken");
    }

    [Fact]
    public void Sse_with_tenancy_and_no_authentication_fails_the_build_naming_the_combination()
    {
        var files = Files(Profile, Sse, "backend.auth=none");
        const string Refusal = "#error frontend.realtime sse with backend.tenancy column needs an identity provider";

        files["src/Host/Realtime/BrowserEventStreams.cs"].ShouldStartWith(Refusal);
        files["tests/Integration.Tests/ServerEventsTests.cs"].ShouldStartWith(Refusal);
    }

    [Fact]
    public void Its_rules_and_adr_describe_the_stream_and_the_query_token()
    {
        var files = Files(Profile, Sse);

        files["AGENTS.md"].ShouldContain("## Realtime: server-sent events");
        files["AGENTS.md"].ShouldContain("IBrowserEvents");
        files["AGENTS.md"].ShouldContain("src/realtime/ServerEvents.tsx");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-realtime-sse.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("access_token");
        adr.ShouldContain("e2e/server-events.spec.ts");
    }

    [Fact]
    public async Task Without_authentication_an_anonymous_browser_opens_the_stream_and_the_project_builds_and_passes_its_checks()
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
            // The stream answers at once, before any event, so a browser knows it is connected.
            var opened = host.OpenStreamAsync("/server-events");
            (await Task.WhenAny(opened, Task.Delay(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken))).ShouldBe(opened);
            using var stream = await opened;
            stream.StatusCode.ShouldBe(HttpStatusCode.OK);
            stream.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
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

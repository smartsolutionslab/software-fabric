using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.realtime: signalr</c> in a Vue app (#238, ADR-0006): the same hub and the same <c>@microsoft/signalr</c>
/// connection as React's, held by a <c>ServerEvents</c> component and listened to with a <c>useServerEvent</c> composable.
/// The backend, the connection and the end-to-end test are React's.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ServerEventsVueTests
{
    private const string Profile = "saas-standard-vue";
    private const string SignalR = "frontend.realtime=signalr";
    private const string Holder = "web/app/src/realtime/ServerEvents.vue";
    private const string Composable = "web/app/src/realtime/useServerEvent.ts";
    private const string Provider = "web/app/src/providers/serverEvents.ts";
    private const string HolderTest = "web/app/src/realtime/ServerEvents.test.ts";
    private const string ComposableTest = "web/app/src/realtime/useServerEvent.test.ts";
    private const string Connection = "web/app/src/realtime/connection.ts";
    private const string EndToEndTest = "web/app/e2e/server-events.spec.ts";

    /// <summary>saas-standard-vue's frontend choices on lean-crud's backend, which has neither tenancy nor a module style.</summary>
    private static readonly string[] Vue =
    [
        "frontend.framework=vue",
        "frontend.routing=vue-router",
        "frontend.forms=vee-validate",
        "frontend.ui-library=shadcn-vue",
    ];

    [Fact]
    public void A_vue_project_with_signalr_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[] { new[] { SignalR }, [SignalR, "frontend.state=tanstack-query"], [SignalR, "frontend.test.extras="] })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }

        Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "lean-crud", [.. Vue, SignalR, "backend.auth=none"]).Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Profile, new[] { SignalR })]
    [InlineData(Profile, new[] { SignalR, "frontend.state=tanstack-query" })]
    [InlineData(Profile, new[] { SignalR, "frontend.test.extras=" })]
    [InlineData("lean-crud", new[] { "frontend.framework=vue", "frontend.routing=vue-router", "frontend.forms=vee-validate", "frontend.ui-library=shadcn-vue", SignalR, "backend.auth=none" })]
    public void No_vue_project_with_signalr_contains_a_react_file(string profile, string[] overrides)
    {
        var files = Files(profile, overrides);

        files.Keys.ShouldNotContain(path => path.EndsWith(".tsx", StringComparison.Ordinal), string.Join(' ', overrides));
        files.Keys.ShouldNotContain("web/app/src/realtime/ServerEventsContext.ts");
        files.Where(file => file.Key.Contains("/realtime/", StringComparison.Ordinal) || file.Key == Provider)
            .ShouldAllBe(file => !file.Value.Contains("react", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_component_holds_the_apps_one_connection_and_components_listen_with_a_composable()
    {
        var files = Files(Profile, [SignalR]);

        files["web/app/package.json"].ShouldContain("\"@microsoft/signalr\": \"10.0.11\"");
        files["web/app/src/main.ts"].ShouldContain("import { withServerEvents } from './providers/serverEvents';");
        files[Provider].ShouldContain("import ServerEvents from '@/realtime/ServerEvents.vue';");
        files[Holder].ShouldContain("<script setup lang=\"ts\">");
        files[Holder].ShouldContain("connectServerEvents()");
        files[Composable].ShouldContain("from 'vue';");
        files[Composable].ShouldContain("export function useServerEvent<T>(name: string, handler: (payload: T) => void)");
        files[HolderTest].ShouldContain("from '@testing-library/vue';");
        files[ComposableTest].ShouldContain("from '@testing-library/vue';");
        files[Connection].ShouldContain("accessTokenFactory");
        files[Connection].ShouldNotContain("throw new Error(");
        files["web/app/vite.config.ts"].ShouldContain("ws: true");
        files.Keys.ShouldContain(EndToEndTest);
        files[EndToEndTest].ShouldNotContain("react-oidc-context");
    }

    [Fact]
    public void Its_rules_and_adr_name_the_vue_files()
    {
        var files = Files(Profile, [SignalR]);

        files["AGENTS.md"].ShouldContain("useServerEvent<InvoicePaidV1>('InvoicePaidV1', handler)");
        files["AGENTS.md"].ShouldContain("src/realtime/ServerEvents.vue");
        files["AGENTS.md"].ShouldNotContain("useCallback");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-realtime-signalr.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("src/realtime/useServerEvent.test.ts");
        adr.ShouldNotContain(".tsx");
    }

    [Fact]
    public void Without_authentication_the_vue_client_sends_no_token()
    {
        var files = Files("lean-crud", [.. Vue, SignalR, "backend.auth=none"]);

        files[Connection].ShouldNotContain("accessToken");
        files[EndToEndTest].ShouldNotContain("Sign In");
    }

    [Fact]
    public void The_vue_web_app_with_signalr_and_tanstack_query_builds_lints_and_passes_its_tests_and_a_server_event_reaches_the_browser()
    {
        using var project = new GeneratedProject(Profile, SignalR, "frontend.state=tanstack-query");
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        // The generated CI's own end-to-end step: the sign-in test and the server event's, against the AppHost.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("2 passed");
    }

    private static Dictionary<string, string> Files(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

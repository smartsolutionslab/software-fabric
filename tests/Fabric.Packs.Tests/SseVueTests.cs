using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.realtime: sse</c> in a Vue app (ADR-0006): the same stream and the same EventSource connection as
/// React's, held by a <c>ServerEvents</c> component and listened to with a <c>useServerEvent</c> composable. The backend,
/// the connection and the end-to-end test are React's.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class SseVueTests
{
    private const string Profile = "saas-standard-vue";
    private const string Sse = "frontend.realtime=sse";
    private const string Holder = "web/app/src/realtime/ServerEvents.vue";
    private const string Composable = "web/app/src/realtime/useServerEvent.ts";
    private const string Provider = "web/app/src/providers/serverEvents.ts";
    private const string HolderTest = "web/app/src/realtime/ServerEvents.test.ts";
    private const string ComposableTest = "web/app/src/realtime/useServerEvent.test.ts";
    private const string Connection = "web/app/src/realtime/connection.ts";
    private const string ConnectionTest = "web/app/src/realtime/connection.test.ts";
    private const string EndToEndTest = "web/app/e2e/server-events.spec.ts";

    [Theory]
    [InlineData(Profile, new[] { Sse })]
    [InlineData(Profile, new[] { Sse, "frontend.state=tanstack-query" })]
    [InlineData("lean-crud", new[] { "frontend.framework=vue", "frontend.routing=vue-router", "frontend.forms=vee-validate", "frontend.ui-library=shadcn-vue", Sse, "backend.auth=none" })]
    public void No_vue_project_with_sse_contains_a_react_file(string profile, string[] overrides)
    {
        var files = Files(profile, overrides);

        files.Keys.ShouldNotContain(path => path.EndsWith(".tsx", StringComparison.Ordinal), string.Join(' ', overrides));
        files.Keys.ShouldNotContain("web/app/src/realtime/ServerEventsContext.ts");
        files.Where(file => file.Key.Contains("/realtime/", StringComparison.Ordinal) || file.Key == Provider)
            .ShouldAllBe(file => !file.Value.Contains("react", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_component_holds_the_apps_one_stream_and_components_listen_with_a_composable()
    {
        var files = Files(Profile, [Sse]);

        files["web/app/src/main.ts"].ShouldContain("import { withServerEvents } from './providers/serverEvents';");
        files[Provider].ShouldContain("import ServerEvents from '@/realtime/ServerEvents.vue';");
        files[Holder].ShouldContain("<script setup lang=\"ts\">");
        files[Holder].ShouldContain("connectServerEvents()");
        files[Composable].ShouldContain("from 'vue';");
        files[Composable].ShouldContain("export function useServerEvent<T>(name: string, handler: (payload: T) => void)");
        files[HolderTest].ShouldContain("from '@testing-library/vue';");
        files[ComposableTest].ShouldContain("from '@testing-library/vue';");
        files[Connection].ShouldContain("new EventSource(");
        files[Connection].ShouldContain("access_token=");
        files[ConnectionTest].ShouldContain("from 'vitest';");
        files.Keys.ShouldContain(EndToEndTest);
        files[EndToEndTest].ShouldNotContain("react-oidc-context");
    }

    [Fact]
    public void Its_rules_and_adr_name_the_vue_files()
    {
        var files = Files(Profile, [Sse]);

        files["AGENTS.md"].ShouldContain("useServerEvent<InvoicePaidV1>('InvoicePaidV1', handler)");
        files["AGENTS.md"].ShouldContain("src/realtime/ServerEvents.vue");
        files["AGENTS.md"].ShouldNotContain("useCallback");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-realtime-sse.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("src/realtime/useServerEvent.test.ts");
        adr.ShouldNotContain(".tsx");
    }

    [Fact]
    public void The_vue_web_app_with_sse_builds_lints_and_passes_its_tests_and_a_server_event_reaches_the_browser()
    {
        using var project = new GeneratedProject(Profile, Sse);
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

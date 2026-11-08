using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.state: pinia</c> in a Vue app (#263): Pinia is the one state library. Stores hold client state and the
/// server data they load through the API client's plain functions; there is no TanStack Query cache, so orval generates
/// plain request functions instead of hooks.
/// </summary>
public sealed class PiniaTests
{
    private const string Profile = "saas-standard-vue";
    private const string Pinia = "frontend.state=pinia";
    private const string Provider = "web/app/src/providers/pinia.ts";

    [Fact]
    public void A_vue_project_with_pinia_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[]
        {
            new[] { Pinia },
            [Pinia, "frontend.api-client=openapi-fetch"],
            [Pinia, "frontend.realtime=signalr"],
            [Pinia, "frontend.dashboards=user-editable"],
            [Pinia, "backend.auth=none", "backend.tenancy=none"],
        })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }
    }

    [Fact]
    public void The_app_installs_pinia_and_no_query_cache_and_orval_generates_plain_functions()
    {
        var files = Files(Pinia);

        files[Provider].ShouldContain("import { createPinia } from 'pinia';");
        files[Provider].ShouldContain("app.use(createPinia());");
        files["web/app/src/main.ts"].ShouldContain("import { withPinia } from './providers/pinia';");
        files["web/app/src/main.ts"].ShouldNotContain("withQueryClient");
        files["web/app/package.json"].ShouldContain("\"pinia\": \"4.1.0\"");
        files["web/app/package.json"].ShouldNotContain("@tanstack/vue-query");
        files["web/app/orval.config.ts"].ShouldContain("client: 'fetch',");
        files["web/app/orval.config.ts"].ShouldNotContain("throw new Error");
        files.Keys.ShouldNotContain("web/app/src/providers/query.ts");
        files.Keys.ShouldNotContain(path => path.EndsWith(".tsx", StringComparison.Ordinal));
    }

    [Fact]
    public void Its_rules_and_adr_put_server_data_in_stores_and_lint_refuses_a_query_cache_but_not_pinia()
    {
        var files = Files(Pinia);

        var lint = files["web/app/eslint.config.js"];
        lint.ShouldContain("'@tanstack/vue-query'");
        lint.ShouldContain("'@pinia/colada'");
        lint.ShouldContain("'vuex'");
        lint.ShouldNotContain("'pinia'");
        files["AGENTS.md"].ShouldContain("## Frontend state: Pinia\n");
        files["AGENTS.md"].ShouldContain("src/stores/");
        files["AGENTS.md"].ShouldNotContain("## Frontend state: Pinia and TanStack Query");
        files["AGENTS.md"].ShouldNotContain("useGetBillingNotes");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-state-pinia.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("src/stores/");
        adr.ShouldContain("getBillingNotes");
        adr.ShouldNotContain("QueryClient");
    }

    /// <summary>The dashboard editor reads through TanStack Query hooks; with Pinia it is left out, the module and its API stay.</summary>
    [Fact]
    public void Dashboards_with_pinia_keep_their_module_and_leave_out_the_editor_built_on_query_hooks()
    {
        var files = Files(Pinia, "frontend.dashboards=user-editable");

        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/dashboards/", StringComparison.Ordinal));
        files.Keys.ShouldContain(path => path.StartsWith("src/Modules/Dashboards/", StringComparison.Ordinal));
        files.Where(file => file.Key.EndsWith(".vue", StringComparison.Ordinal)).ShouldAllBe(file => !file.Value.Contains("DashboardEditor"));
    }

    [Fact]
    public void The_vue_web_app_builds_lints_and_passes_its_tests_with_a_store_loading_server_data_and_lint_refuses_vue_query()
    {
        using var project = new GeneratedProject(Profile, Pinia);
        // Each source ends with a newline, as Prettier writes it, so the format check sees the files a developer would commit.
        project.Write("web/app/src/stores/currentUser.ts", """
            import { defineStore } from 'pinia';
            import { ref } from 'vue';
            import { type CurrentUser, getMe } from '@/api/generated';

            export const useCurrentUser = defineStore('currentUser', () => {
              const user = ref<CurrentUser>();

              async function load() {
                user.value = await getMe();
              }

              return { user, load };
            });

            """);
        project.Write("web/app/src/stores/currentUser.test.ts", """
            import { createPinia, setActivePinia } from 'pinia';
            import { afterEach, beforeEach, expect, test, vi } from 'vitest';
            import { useCurrentUser } from '@/stores/currentUser';

            beforeEach(() => {
              setActivePinia(createPinia());
            });

            afterEach(() => {
              vi.unstubAllGlobals();
            });

            test('a store loads server data through the generated function and holds it', async () => {
              const fetch = vi.fn<typeof globalThis.fetch>(() =>
                Promise.resolve(new Response(JSON.stringify({ name: 'developer' }), { status: 200 })),
              );
              vi.stubGlobal('fetch', fetch);
              const currentUser = useCurrentUser();

              await currentUser.load();

              expect(fetch.mock.calls[0]?.[0]).toBe('/api/me');
              expect(currentUser.user?.name).toBe('developer');
            });

            """);

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        project.Write("web/app/src/cache.ts", """
            import { QueryClient } from '@tanstack/vue-query';

            export const cache = new QueryClient();
            """);

        var (lintExitCode, lint) = Shell.Run(web, "pnpm", "lint");

        lintExitCode.ShouldNotBe(0, lint);
        lint.ShouldContain("'@tanstack/vue-query' import is restricted from being used by a pattern. Server data and client state: Pinia stores");
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

namespace Fabric.Packs.Tests;

/// <summary>
/// saas-standard-vue's web app from the real Vue packs: Vue with vue-router, Pinia and TanStack Query, an orval client generated for
/// vue-query, zod, the axe check, the Vue branches of the sign-in, table and chart packs (#159), and vee-validate with Reka UI
/// and Tailwind (#160).
/// </summary>
public sealed class VueFrontendTests
{
    private const string Profile = "saas-standard-vue";

    /// <summary>A test a developer would write: a generated vue-query hook reaches the host through /api and its answer renders.</summary>
    private const string QueryHookTest = """
        import { VueQueryPlugin } from '@tanstack/vue-query';
        import { render, screen } from '@testing-library/vue';
        import { afterEach, expect, test, vi } from 'vitest';
        import { defineComponent, h } from 'vue';
        import { useGetMe } from '@/api/generated';

        afterEach(() => {
          vi.unstubAllGlobals();
        });

        const Me = defineComponent({
          setup() {
            const { data } = useGetMe();
            return () => h('p', data.value ? `The host knows ${data.value.name}` : 'Loading…');
          },
        });

        test('a generated query hook reaches the host through /api and renders its answer', async () => {
          const fetch = vi.fn<typeof globalThis.fetch>(() =>
            Promise.resolve(new Response(JSON.stringify({ name: 'developer' }), { status: 200 })),
          );
          vi.stubGlobal('fetch', fetch);

          render(Me, { global: { plugins: [VueQueryPlugin] } });

          expect(await screen.findByText('The host knows developer')).toBeInTheDocument();
          expect(fetch.mock.calls[0]?.[0]).toBe('/api/me');
        });
        """;

    /// <summary>A module's screen is found by convention: its link is in the navigation and leads to its page.</summary>
    private const string ModuleRouteTest = """
        import { render, screen } from '@testing-library/vue';
        import userEvent from '@testing-library/user-event';
        import { expect, test } from 'vitest';
        import { createMemoryHistory } from 'vue-router';
        import App from '@/App.vue';
        import { createAppRouter } from '@/router';

        test('the billing link leads to the billing page', async () => {
          const router = createAppRouter(createMemoryHistory());
          await router.push('/');
          render(App, { global: { plugins: [router] } });

          await userEvent.click(screen.getByRole('link', { name: 'Billing' }));

          expect(await screen.findByRole('heading', { name: 'Billing' })).toBeInTheDocument();
        });
        """;

    [Fact]
    public void The_vue_web_app_installs_builds_lints_and_passes_its_tests()
    {
        using var project = Generate();
        project.AddModule("billing");

        RunWeb(project, "install", "build", "lint", "test");
    }

    [Fact]
    public void The_vue_web_app_passes_its_format_check_after_building()
    {
        using var project = Generate();
        project.AddModule("billing");
        RunWeb(project, "install", "build");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");

        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
    }

    [Fact]
    public void A_generated_vue_query_hook_reaches_the_host_through_the_api_prefix()
    {
        using var project = Generate();
        project.Write("web/app/src/api/me.test.ts", QueryHookTest);

        RunWeb(project, "install", "test");
    }

    [Fact]
    public void A_modules_page_is_reached_through_its_link_without_a_list_to_edit()
    {
        using var project = Generate();
        project.AddModule("billing");
        project.Write("web/app/src/modules/billing/route.test.ts", ModuleRouteTest);

        RunWeb(project, "install", "test");
    }

    [Fact]
    public void A_screen_with_an_accessibility_violation_fails_the_tests()
    {
        using var project = Generate();
        project.Write("web/app/src/pages/Home.vue", """
            <template>
              <main>
                <h1>shop</h1>
                <img src="https://example.com/logo.png" />
              </main>
            </template>
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "test");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("accessibility.test.ts");
        output.ShouldContain("Images must have alternative text");
    }

    [Fact]
    public void Importing_vuex_fails_lint()
    {
        using var project = Generate();
        project.Write("web/app/src/cart.ts", """
            import { createStore } from 'vuex';

            export const cart = createStore({ state: { items: 0 } });
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'vuex' import is restricted from being used by a pattern. Server data: the generated TanStack Query hooks; client state: Pinia stores");
    }

    [Fact]
    public void Importing_another_schema_library_fails_lint_in_a_vue_component()
    {
        using var project = Generate();
        project.Write("web/app/src/components/SignupForm.vue", """
            <script setup lang="ts">
            import { object, string } from 'yup';

            const schema = object({ email: string().required() });
            </script>

            <template>
              <p>{{ schema.fields.email ? 'email' : '' }}</p>
            </template>
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'yup' import is restricted from being used by a pattern. Use zod (docs/adr)");
    }

    [Fact]
    public void A_client_generated_for_vue_without_tanstack_query_fails_loudly()
    {
        using var project = Generate("frontend.state=pinia");
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("The orval pack generates TanStack Query hooks for Vue with frontend.state tanstack-query or pinia-tanstack-query only so far");
    }

    private static GeneratedProject Generate(params string[] overrides) => new(Profile, overrides);

    private static void RunWeb(GeneratedProject project, params string[] steps)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in steps)
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }
    }
}

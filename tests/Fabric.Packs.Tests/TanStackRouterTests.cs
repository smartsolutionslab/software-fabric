using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.routing.tanstack-router (#265): TanStack Router with routes in code, each module's route found by glob as
/// react-router finds it, and the same module pages for micro-frontends, the dashboards module and the assistant module.
/// </summary>
public sealed class TanStackRouterTests
{
    private const string Profile = "saas-standard-react";
    private const string TanStackRouter = "frontend.routing=tanstack-router";

    /// <summary>A module's screen is found by convention: its link is in the navigation and leads to its page.</summary>
    private const string ModuleRouteTest = """
        import { render, screen } from '@testing-library/react';
        import userEvent from '@testing-library/user-event';
        import { createMemoryHistory, RouterProvider } from '@tanstack/react-router';
        import { expect, test } from 'vitest';
        import { App } from '@/App';
        import { createAppRouter } from '@/router';

        test('the billing link leads to the billing page', async () => {
          const router = createAppRouter(createMemoryHistory(), <App />);
          render(<RouterProvider router={router} />);

          await userEvent.click(await screen.findByRole('link', { name: 'Billing' }));

          expect(await screen.findByRole('heading', { name: 'Billing' })).toBeInTheDocument();
        });
        """;

    [Fact]
    public void The_web_app_installs_builds_lints_passes_its_tests_and_its_format_check_with_a_module()
    {
        using var project = new GeneratedProject(Profile, TanStackRouter);
        project.AddModule("billing");

        RunWeb(project, "install", "build", "lint", "test");
        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
    }

    [Fact]
    public void A_modules_page_is_reached_through_its_link_without_a_list_to_edit()
    {
        using var project = new GeneratedProject(Profile, TanStackRouter);
        project.AddModule("billing");
        project.Write("web/app/src/modules/billing/route.test.tsx", ModuleRouteTest);

        RunWeb(project, "install", "test");
    }

    [Fact]
    public void The_app_depends_on_tanstack_router_and_not_on_react_router()
    {
        using var project = new GeneratedProject(Profile, TanStackRouter);
        var packageJson = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "package.json"));

        packageJson.ShouldContain("\"@tanstack/react-router\":");
        packageJson.ShouldNotContain("\"react-router\":");
    }

    [Fact]
    public void Importing_react_router_or_file_based_routing_fails_lint()
    {
        using var project = new GeneratedProject(Profile, TanStackRouter);
        project.Write("web/app/src/other-router.ts", """
            import { Link } from 'react-router';
            import { NavLink } from 'react-router-dom';

            export const links = [Link, NavLink];
            """);
        project.Write("web/app/src/file-routes.ts", """
            import { tanstackRouter } from '@tanstack/router-plugin/vite';

            export const plugin = tanstackRouter;
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'react-router' import is restricted from being used by a pattern. Use @tanstack/react-router (docs/adr)");
        output.ShouldContain("'react-router-dom' import is restricted from being used by a pattern. Use @tanstack/react-router (docs/adr)");
        output.ShouldContain("'@tanstack/router-plugin/vite' import is restricted from being used by a pattern. Routes are declared in src/router.tsx and src/modules/*/routes.tsx (docs/adr)");
    }

    [Fact]
    public void The_assistant_and_dashboards_modules_get_their_choices_pages()
    {
        using var project = new GeneratedProject("saas-ai-react", TanStackRouter);
        var modules = Path.Combine(project.ProjectDirectory, "web", "app", "src", "modules");

        File.ReadAllText(Path.Combine(modules, "dashboards", "DashboardsPage.tsx")).ShouldContain("<DashboardEditor />");
        File.ReadAllText(Path.Combine(modules, "assistant", "AssistantPage.tsx")).ShouldContain("<AgentPanel agent=\"assistant\" />");
        File.ReadAllText(Path.Combine(modules, "assistant", "routes.tsx")).ShouldContain("component: AssistantPage");
    }

    [Fact]
    public void With_micro_frontends_a_modules_page_mounts_its_remote()
    {
        using var project = new GeneratedProject("industrial-event-driven-mfe", TanStackRouter);
        project.AddModule("billing");

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "modules", "billing", "BillingPage.tsx"))
            .ShouldContain("<Remote module=\"billing\" />");
    }

    /// <summary>As react-router's: the module page names the dashboard editor exactly where the editor is generated.</summary>
    [Theory]
    [InlineData("spa")]
    [InlineData("multi-app")]
    public void The_module_page_renders_the_dashboard_editor_only_where_the_editor_is_generated(string architecture)
    {
        var router = Repository.Packs.Packs["frontend.routing.tanstack-router"];
        var dashboards = Repository.Packs.Packs["frontend.dashboards.user-editable"];
        var choices = new ProjectChoices(
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["frontend.framework"] = ["react"],
                ["frontend.architecture"] = [architecture],
                ["frontend.api-client"] = ["rtk-query-codegen"],
                ["frontend.dashboards"] = ["user-editable"],
            },
            new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> { ["dashboards"] = new Dictionary<string, IReadOnlyList<string>>() });
        const string Page = "web/app/src/modules/dashboards/DashboardsPage.tsx";
        const string Editor = "web/app/src/dashboards/DashboardEditor.tsx";
        List<ComposedFile> files =
        [
            new(router, router.Files.Single(file => file.Template == "templates/module-page.tsx.sbn"), Page, "dashboards"),
            new(dashboards, dashboards.Files.Single(file => file.Target == Editor), Editor, null),
        ];

        var rendered = Renderer.Render(Repository.Catalog, new Composition([router, dashboards], files, new Dictionary<string, IReadOnlyList<SlotEntry>>(), []), choices, "shop");

        rendered.Errors.ShouldBeEmpty();
        var generated = rendered.Files.ToDictionary(file => file.Path, file => file.Content);
        generated[Page].Contains("DashboardEditor", StringComparison.Ordinal).ShouldBe(generated.ContainsKey(Editor), architecture);
    }

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

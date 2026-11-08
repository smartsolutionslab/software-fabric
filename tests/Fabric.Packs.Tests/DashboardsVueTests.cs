using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.dashboards: [user-editable]</c> in a Vue single-page app (ADR-0015, #214): the same dashboard page as
/// React's, on <c>grid-layout-plus</c>, the Orval-generated TanStack Query client and the project's shadcn-vue
/// components, on the <c>dashboards</c> module's page. The framework-free helpers and the end-to-end test are React's.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class DashboardsVueTests
{
    private const string Profile = "saas-standard-vue";
    private const string Dashboards = "frontend.dashboards=user-editable";
    private const string Page = "web/app/src/modules/dashboards/DashboardsPage.vue";
    private const string Editor = "web/app/src/dashboards/DashboardEditor.vue";
    private const string Canvas = "web/app/src/dashboards/DashboardCanvas.vue";
    private const string EditorTest = "web/app/src/dashboards/DashboardEditor.test.ts";
    private const string EndToEndTest = "web/app/e2e/dashboards.spec.ts";

    /// <summary>The files React's page and Vue's share: they use neither framework.</summary>
    private static readonly string[] Shared =
    [
        "web/app/src/dashboards/layout.ts",
        "web/app/src/dashboards/layout.test.ts",
        "web/app/src/dashboards/chartOption.ts",
        "web/app/src/dashboards/chartOption.test.ts",
        "web/app/src/dashboards/uuidV7.ts",
        "web/app/src/dashboards/uuidV7.test.ts",
        "web/app/src/dashboards/widgetChoice.ts",
        "web/app/src/dashboards/widgetChoice.test.ts",
    ];

    [Fact]
    public void A_vue_project_with_user_editable_dashboards_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[] { new[] { Dashboards }, [Dashboards, "frontend.test.extras="] })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }
    }

    [Fact]
    public void The_vue_spa_gets_the_dashboard_page_on_grid_layout_plus_and_the_module_page_renders_it()
    {
        var files = Files(Dashboards);

        files["web/app/package.json"].ShouldContain("\"grid-layout-plus\": \"1.1.1\"");
        files["web/app/package.json"].ShouldNotContain("react");
        files[Page].ShouldContain("import DashboardEditor from '@/dashboards/DashboardEditor.vue';");
        files[Page].ShouldContain("<DashboardEditor />");
        files[Canvas].ShouldContain("from 'grid-layout-plus';");
        files[Canvas].ShouldContain("import { Button } from '@/components/ui/button';");
        files[Canvas].ShouldContain("import { useForm } from 'vee-validate';");
        files[Canvas].ShouldContain("usePutDashboardsId");
        files[Editor].ShouldContain("useGetDashboards(");
        files["web/app/src/dashboards/WidgetView.vue"].ShouldContain("import DataChart from '@/components/DataChart.vue';");
        files["web/app/src/dashboards/WidgetView.vue"].ShouldContain("import DataTable from '@/components/ui/DataTable.vue';");
        files.Keys.ShouldContain("web/app/src/dashboards/Pager.vue");
        files.Keys.ShouldContain("web/app/src/dashboards/Pager.test.ts");
        files.Keys.ShouldContain("web/app/src/dashboards/Failure.vue");
        files.Keys.ShouldContain(EditorTest);
        files.Keys.ShouldContain(EndToEndTest);
        files.Keys.ShouldNotContain(path => path.EndsWith(".tsx", StringComparison.Ordinal));
        files["web/app/src/index.css"].ShouldNotContain("grid-layout");
        files["AGENTS.md"].ShouldContain("DashboardEditor.vue");
        files["docs/adr/" + Adr(files)].ShouldContain("grid-layout-plus 1.1.1");
    }

    [Fact]
    public void The_framework_free_helpers_and_their_tests_are_the_same_files_as_reacts()
    {
        var vue = Files(Dashboards);
        var react = Generated("saas-standard-react", Dashboards);

        foreach (var path in Shared)
        {
            vue[path].ShouldBe(react[path].Replace("react-grid-layout", "grid-layout-plus", StringComparison.Ordinal), path);
        }
    }

    [Fact]
    public void The_end_to_end_test_is_reacts_on_grid_layout_plus_class_names()
    {
        var vue = Files(Dashboards)[EndToEndTest];
        var react = Generated("saas-standard-react", Dashboards)[EndToEndTest];

        vue.ShouldContain("page.locator('.vgl-layout')");
        vue.ShouldContain("widget.locator('xpath=ancestor::*[contains(@class, \"vgl-item\")]').locator('.vgl-item__resizer');");
        vue.ShouldNotContain("react");
        WithoutTheGrid(vue).ShouldBe(WithoutTheGrid(react));
    }

    /// <summary>The spec without the lines that find the grid, its items and their resize handles by class name.</summary>
    private static string WithoutTheGrid(string spec) =>
        string.Join('\n', spec.Split('\n').Where(line => !line.Contains("const grid = ", StringComparison.Ordinal) && !line.Contains("const handle = ", StringComparison.Ordinal) && !line.TrimStart().StartsWith(".locator(", StringComparison.Ordinal)));

    [Fact]
    public void The_vue_page_checks_accessibility_with_axe_only_where_the_project_has_the_accessibility_tests()
    {
        Files(Dashboards)[EditorTest].ShouldContain("from 'vitest-axe'");

        var without = Files(Dashboards, "frontend.test.extras=");

        without["web/app/package.json"].ShouldNotContain("vitest-axe");
        without[EditorTest].ShouldNotContain("axe");
    }

    [Fact]
    public void Another_vue_ui_library_fails_the_page_saying_it_is_not_built_for_it_yet()
    {
        var files = Files(Dashboards, "frontend.ui-library=reka-ui");

        files[Canvas].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The dashboards pack builds the Vue dashboard page from shadcn-vue with vee-validate or TanStack Form and zod or valibot only so far; frontend.ui-library is reka-ui");
    }

    /// <summary>
    /// Only <c>spa</c> has a pack today, so no profile can reach another architecture; the two packs' templates are rendered
    /// with a project that chooses it, to show the Vue module page names the editor exactly where the editor is generated.
    /// </summary>
    [Theory]
    [InlineData("spa")]
    [InlineData("multi-app")]
    public void The_vue_module_page_renders_the_dashboard_editor_only_where_the_editor_is_generated(string architecture)
    {
        var router = Repository.Packs.Packs["frontend.routing.vue-router"];
        var dashboards = Repository.Packs.Packs["frontend.dashboards.user-editable"];
        var choices = new ProjectChoices(
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["frontend.framework"] = ["vue"],
                ["frontend.architecture"] = [architecture],
                ["frontend.api-client"] = ["orval"],
                ["frontend.dashboards"] = ["user-editable"],
            },
            new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> { ["dashboards"] = new Dictionary<string, IReadOnlyList<string>>() });
        List<ComposedFile> files =
        [
            new(router, router.Files.Single(file => file.Template == "templates/module-page.vue.sbn"), Page, "dashboards"),
            new(dashboards, dashboards.Files.Single(file => file.Target == "web/app/src/dashboards/DashboardEditor.vue"), Editor, null),
        ];

        var rendered = Renderer.Render(Repository.Catalog, new Composition([router, dashboards], files, new Dictionary<string, IReadOnlyList<SlotEntry>>(), []), choices, "shop");

        rendered.Errors.ShouldBeEmpty();
        var generated = rendered.Files.ToDictionary(file => file.Path, file => file.Content);
        generated[Page].Contains("DashboardEditor", StringComparison.Ordinal).ShouldBe(generated.ContainsKey(Editor), architecture);
    }

    [Fact]
    public void The_generated_vue_web_app_builds_lints_and_passes_its_tests_and_the_end_to_end_test_finds_a_saved_dashboard_after_a_reload()
    {
        using var project = new GeneratedProject(Profile, Dashboards);
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("2 passed");
    }

    /// <summary>The generated ADR of the dashboards option, whatever number its place in the selection gives it.</summary>
    private static string Adr(Dictionary<string, string> files) =>
        Path.GetFileName(files.Keys.Single(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-dashboards-user-editable.md", StringComparison.Ordinal)));

    private static Dictionary<string, string> Files(params string[] overrides) => Generated(Profile, overrides);

    private static Dictionary<string, string> Generated(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

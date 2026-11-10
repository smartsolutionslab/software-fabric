using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #273 (M3-51): frontend.ui-library.vuetify, with library-theme's Vue branch. Under library-theme the library's own
/// theme object lives in one generated file, <c>src/theme.ts</c>, with no Tailwind; the Vue dashboards, the agent panel
/// and the other screens that wrote Tailwind classes are built from Vuetify's components instead, whatever the styling;
/// AG Grid takes its colours from Vuetify's theme (option C of #274). Every other profile keeps its output byte for byte.
/// </summary>
public sealed partial class VuetifyTests
{
    private const string Vue = "saas-standard-vue";
    private static readonly string[] Vuetify = ["frontend.ui-library=vuetify", "frontend.styling=library-theme"];
    private static readonly string[] AgentsAndDashboards =
        ["backend.llm=anthropic", "backend.llm.agents=agent-framework", "frontend.agent-ui=ag-ui", "frontend.dashboards=user-editable,generative"];

    private static readonly string[] VuetifyWithAgents = [.. Vuetify, .. AgentsAndDashboards];

    private static readonly string[] VuetifyOtherChoices =
        [.. VuetifyWithAgents, "frontend.forms=tanstack-form", "frontend.validation=valibot", "frontend.data-grid=ag-grid", "frontend.charts=chartjs"];

    private static readonly string[] VuetifyFormKitTokensAndMsal =
        [.. VuetifyWithAgents, "frontend.forms=formkit", "frontend.design-system=tokens", "backend.auth=entra-id", "frontend.auth-client=msal"];

    private static readonly string[] VuetifyCssModules =
        ["frontend.ui-library=vuetify", "frontend.styling=css-modules", .. AgentsAndDashboards];

    private static readonly string[] VuetifyTailwind =
        ["frontend.ui-library=vuetify", "frontend.styling=tailwind", .. AgentsAndDashboards];

    /// <summary>The screens that used shadcn-vue's or Reka UI's button.</summary>
    private static readonly string[] Screens =
    [
        "web/app/src/agents/AgentConversation.vue",
        "web/app/src/dashboards/DashboardAssistant.vue",
        "web/app/src/dashboards/DashboardCanvas.vue",
        "web/app/src/dashboards/DashboardEditor.vue",
        "web/app/src/dashboards/Failure.vue",
        "web/app/src/dashboards/Pager.vue",
        "web/app/src/dashboards/PendingWidget.vue",
        "web/app/src/dashboards/PlacedWidget.vue",
    ];

    /// <summary>A screen a developer might write, which colours itself instead of taking the colour from the theme.</summary>
    private const string OwnColour = """
        <template>
          <p style="color: #d32f2f">Alarm</p>
        </template>
        """;

    private const string OtherLibraries = """
        import { Button } from 'primevue/button';
        import { ElButton } from 'element-plus';
        import { DialogRoot } from 'reka-ui';

        export const others = [Button, ElButton, DialogRoot];
        """;

    [Fact]
    public void A_vue_project_with_vuetify_and_the_library_theme_generates_from_the_real_packs_alone()
    {
        var files = Files(Vuetify);

        var package = files["web/app/package.json"];
        package.ShouldContain("\"vuetify\": \"4.3.2\"");
        package.ShouldContain("\"@vue/test-utils\": \"2.5.1\"");
        foreach (var tailwind in new[] { "tailwindcss", "tailwind-merge", "tw-animate-css", "class-variance-authority", "reka-ui", "clsx" })
        {
            package.ShouldNotContain(tailwind);
        }

        files.Keys.ShouldNotContain("web/app/components.json");
        files.Keys.ShouldNotContain("web/app/src/lib/utils.ts");
        files.Keys.Where(path => path.StartsWith("web/app/src/components/ui/", StringComparison.Ordinal) && path.Contains("Button", StringComparison.Ordinal)).ShouldBeEmpty();

        var theme = files["web/app/src/theme.ts"];
        theme.ShouldContain("import type { VuetifyOptions } from 'vuetify';");
        theme.ShouldContain("} satisfies VuetifyOptions['theme'];");
        theme.ShouldContain("primary: '#2563eb',");
        files["web/app/src/vuetify.ts"].ShouldContain("export const vuetify = createVuetify({ theme });");
        files.Keys.ShouldContain("web/app/src/theme.test.ts");

        var main = files["web/app/src/main.ts"];
        main.ShouldContain("import 'vuetify/styles';");
        main.ShouldContain("import { withVuetify } from './providers/vuetify';");
        main.ShouldContain("// Outermost first: each one wraps all that follow it.\n  withVuetify,\n", customMessage: "the sign-in and every page are inside Vuetify's VApp");
        files["web/app/src/providers/vuetify.ts"].ShouldContain("h(VApp, null, { default: () => h(root) })");

        files["web/app/src/test-setup.ts"].ShouldContain("config.global.plugins.push(vuetify);");
        var vite = files["web/app/vite.config.ts"];
        vite.ShouldContain("deps: { inline: ['vuetify'] }");
        vite.ShouldNotContain("tailwind");
        files["web/app/src/index.css"].ShouldNotContain("tailwind");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use Vuetify (vuetify/components) and the theme in src/theme.ts (docs/adr).'");

        var agents = files["AGENTS.md"];
        agents.ShouldContain("## UI components: Vuetify");
        agents.ShouldContain("## Styling: the library's theme");
        agents.ShouldContain("the Vuetify theme in `src/theme.ts`");
        agents.ShouldNotContain("shadcn");
        agents.ShouldNotContain("Tailwind");
        agents.ShouldNotContain("src/index.css` theme variables");
        Adr(files, "frontend-ui-library-vuetify").ShouldContain("# UI components: Vuetify");
        Adr(files, "frontend-styling-library-theme").ShouldContain("Vuetify's own theming system");
        Adr(files, "frontend-design-system-none").ShouldNotContain("Tailwind");
    }

    [Fact]
    public void With_vuetify_and_the_tokens_the_theme_is_built_from_the_tokens_instead_of_colours_of_its_own()
    {
        var theme = Files([.. Vuetify, "frontend.design-system=tokens"])["web/app/src/theme.ts"];

        theme.ShouldContain("import { tokens } from '@shop/tokens';");
        theme.ShouldContain("primary: tokens.color.primary,");
        theme.ShouldContain("'on-primary': tokens.color['primary-foreground'],");
        theme.ShouldContain("secondary: tokens.color.accent,");
        theme.ShouldNotContain("#", customMessage: "a colour of the theme's own");
    }

    public static TheoryData<string[]> VuetifyProjects => new()
    {
        VuetifyWithAgents,
        VuetifyOtherChoices,
        VuetifyFormKitTokensAndMsal,
        VuetifyCssModules,
        VuetifyTailwind,
    };

    [Theory]
    [MemberData(nameof(VuetifyProjects))]
    public void With_vuetify_no_vue_source_carries_a_tailwind_class_a_css_module_or_a_shadcn_vue_or_reka_ui_component(string[] overrides)
    {
        var files = Files(overrides);

        var sources = files.Where(file => file.Key.StartsWith("web/app/src/", StringComparison.Ordinal) && file.Key.EndsWith(".vue", StringComparison.Ordinal)).ToList();
        sources.ShouldNotBeEmpty();
        foreach (var (path, content) in sources)
        {
            content.ShouldNotContain("@/components/ui/button", customMessage: path);
            content.ShouldNotContain("BaseButton", customMessage: path);
            content.ShouldNotContain("@/lib/utils", customMessage: path);
            content.ShouldNotContain("styles.", customMessage: path);
            foreach (Match attribute in ClassAttribute().Matches(content))
            {
                var tailwind = attribute.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(name => TailwindOnly().IsMatch(name)).ToList();
                tailwind.ShouldBeEmpty($"{path}: {attribute.Value}");
            }
        }

        files.Keys.Where(path => path.EndsWith(".module.css", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(VuetifyProjects))]
    public void With_vuetify_the_dashboards_and_the_agent_panel_are_built_from_vuetify_components(string[] overrides)
    {
        var files = Files(overrides);

        foreach (var screen in Screens)
        {
            var content = files[screen];
            content.ShouldNotStartWith("<script lang=\"ts\">\nthrow new Error", customMessage: screen);
            content.ShouldMatch(@"import \{[^}]*\bVBtn\b[^}]*\} from 'vuetify/components';", customMessage: screen);
            content.ShouldContain("<VBtn", customMessage: screen);
        }
    }

    [Fact]
    public void With_vuetify_formkit_styles_its_inputs_with_vuetify_classes()
    {
        var config = Files(VuetifyFormKitTokensAndMsal)["web/app/src/forms/formkit.config.ts"];

        config.ShouldNotStartWith("throw new Error");
        config.ShouldContain("rounded border px-2 py-1");
    }

    [Theory]
    [InlineData("frontend.styling=library-theme")]
    [InlineData("frontend.styling=css-modules")]
    public void With_vuetify_ag_grid_takes_its_colours_from_the_vuetify_theme(string styling)
    {
        var files = Files("frontend.ui-library=vuetify", styling, "frontend.data-grid=ag-grid");

        var columns = files["web/app/src/components/ui/data-table-columns.ts"];
        columns.ShouldContain("""
            export const theme = themeQuartz.withParams({
              fontFamily: 'inherit',
              backgroundColor: 'rgb(var(--v-theme-surface))',
              foregroundColor: 'rgb(var(--v-theme-on-surface))',
              borderColor: 'rgba(var(--v-border-color), var(--v-border-opacity))',
              headerBackgroundColor: 'rgba(var(--v-theme-on-surface), var(--v-hover-opacity))',
              accentColor: 'rgb(var(--v-theme-primary))',
            });
            """);
        files["AGENTS.md"].ShouldContain("The grid's colours, borders and font come from Vuetify's theme");
        Adr(files, "frontend-data-grid-ag-grid").ShouldContain("Quartz theme bound to Vuetify's theme variables");
    }

    [Fact]
    public void The_vuetify_web_app_with_dashboards_and_the_agent_panel_builds_lints_is_formatted_and_passes_its_tests_and_lint_refuses_other_libraries()
    {
        using var project = new GeneratedProject(Vue, VuetifyWithAgents);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/theme.test.ts > Vuetify's components take their colours from the theme", customMessage: testOutput);
        plain.ShouldContain("✓ src/theme.test.ts > no component writes a colour of its own", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.ts", customMessage: testOutput);
        FormatIsClean(project);

        project.Write("web/app/src/pages/otherLibraries.ts", OtherLibraries);
        var (exitCode, lintOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, lintOutput);
        foreach (var library in new[] { "primevue/button", "element-plus", "reka-ui" })
        {
            lintOutput.ShouldContain($"'{library}' import is restricted from being used by a pattern. Use Vuetify (vuetify/components) and the theme in src/theme.ts (docs/adr)");
        }
    }

    [Fact]
    public void The_vuetify_web_app_with_other_forms_validation_grid_and_charts_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Vue, VuetifyOtherChoices);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/components/ui/DataTable.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.ts", customMessage: testOutput);
        FormatIsClean(project);
    }

    [Fact]
    public void The_vuetify_web_app_with_formkit_the_design_tokens_and_msal_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Vue, VuetifyFormKitTokensAndMsal);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/theme.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/auth/SignedIn.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts", customMessage: testOutput);
        FormatIsClean(project);
    }

    [Fact]
    public void The_vuetify_web_app_with_css_modules_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Vue, VuetifyCssModules);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.ts", customMessage: testOutput);
        FormatIsClean(project);
    }

    [Fact]
    public void A_component_that_writes_a_colour_of_its_own_fails_the_theme_check_naming_it()
    {
        using var project = new GeneratedProject(Vue, Vuetify);
        RunWeb(project, "install");
        project.Write("web/app/src/pages/AlarmBanner.vue", OwnColour);

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "test", "src/theme.test.ts");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("pages/AlarmBanner.vue");
    }

    [Fact]
    public void A_vuetify_that_does_not_take_the_theme_fails_the_theme_check()
    {
        using var project = new GeneratedProject(Vue, Vuetify);
        RunWeb(project, "install");
        project.Write("web/app/src/vuetify.ts", "import { createVuetify } from 'vuetify';\n\nexport const vuetify = createVuetify();\n");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "test", "src/theme.test.ts");

        exitCode.ShouldNotBe(0, output);
        Escapes().Replace(output, string.Empty).ShouldContain("FAIL  src/theme.test.ts > Vuetify's components take their colours from the theme", customMessage: output);
    }

    private static void FormatIsClean(GeneratedProject project)
    {
        var (exitCode, format) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, format);
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Vue, overrides);
        generated.Errors.ShouldBeEmpty($"{Vue} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static string Adr(Dictionary<string, string> files, string option) =>
        files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith($"-{option}.md", StringComparison.Ordinal)).Value;

    private static void RunWeb(GeneratedProject project, params string[] steps)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in steps)
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }
    }

    /// <summary>Installs, builds and lints the web workspace, runs the app's tests, and returns their verbose output.</summary>
    private static string InstallBuildLintAndTest(GeneratedProject project)
    {
        RunWeb(project, "install", "build", "lint");

        var (testExitCode, testOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        return testOutput;
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();

    [GeneratedRegex(@"(?<![:\w-])class=""([^""]*)""")]
    private static partial Regex ClassAttribute();

    /// <summary>A Tailwind class Vuetify has no class of that name for.</summary>
    [GeneratedRegex(@"^(?:[a-z]+:)*(?:flex|flex-col|flex-1|items-(?:start|center|end)|justify-between|gap-\d+|space-[xy]-\d+|text-(?:xs|sm|lg|xl|\dxl)|font-(?:medium|semibold)|bg-(?:card|white|muted|slate-\d+)|text-(?:muted-foreground|slate-\d+)|min-h-0|p-\d+|rounded-(?:sm|md|lg)|whitespace-pre-wrap)$")]
    private static partial Regex TailwindOnly();
}

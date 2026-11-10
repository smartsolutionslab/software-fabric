using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #272 (M3-50): frontend.ui-library.mui with frontend.styling.library-theme. The library's own theme object lives in one
/// generated file, <c>src/theme.ts</c>, with no Tailwind; the React dashboards, the agent panel and the other screens that
/// wrote Tailwind classes are built from MUI's components instead; AG Grid takes its colours from that theme (option C of
/// #274). Every other profile keeps its output byte for byte.
/// </summary>
public sealed partial class MuiTests
{
    private const string React = "saas-standard-react";
    private const string WithAgents = "saas-ai-react";
    private static readonly string[] Mui = ["frontend.ui-library=mui", "frontend.styling=library-theme"];
    private static readonly string[] MuiOtherChoices =
        [.. Mui, "frontend.forms=tanstack-form", "frontend.validation=valibot", "frontend.data-grid=ag-grid", "frontend.charts=recharts"];

    private static readonly string[] MuiRouterAndMsal =
        [.. Mui, "frontend.routing=tanstack-router", "backend.auth=entra-id", "frontend.auth-client=msal"];

    /// <summary>A screen a developer might write, which colours itself instead of taking the colour from the theme.</summary>
    private const string OwnColour = """
        import { Typography } from '@mui/material';

        export function Alarm() {
          return <Typography sx={{ color: '#d32f2f' }}>Alarm</Typography>;
        }
        """;

    private const string ResolveColour = """
        /** A value as the browser paints it: MUI sets some through its own custom properties. */
        function resolve(style: CSSStyleDeclaration, value: string): string {
          const custom = /^var\((--[\w-]+)\)$/.exec(value)?.[1];
          return custom === undefined ? value : resolve(style, style.getPropertyValue(custom).trim());
        }
        """;

    /// <summary>The app's navigation with React Router, under the theme whose primary colour the test changed.</summary>
    private const string ReactRouterLinks = $$"""
        import { ThemeProvider } from '@mui/material/styles';
        import { render, screen } from '@testing-library/react';
        import { MemoryRouter } from 'react-router';
        import { expect, test } from 'vitest';
        import { App } from './App';
        import { theme } from './theme';

        {{ResolveColour}}

        test("the navigation links take the theme's primary colour", () => {
          render(
            <ThemeProvider theme={theme}>
              <MemoryRouter>
                <App />
              </MemoryRouter>
            </ThemeProvider>,
          );

          const style = getComputedStyle(screen.getByRole('link', { name: 'Home' }));
          // #0b6e4f, as the browser serialises it.
          expect(resolve(style, style.color)).toBe('rgb(11, 110, 79)');
        });
        """;

    /// <summary>The app's navigation with TanStack Router, under the theme whose primary colour the test changed.</summary>
    private const string TanStackLinks = $$"""
        import { ThemeProvider } from '@mui/material/styles';
        import { render, screen } from '@testing-library/react';
        import { createMemoryHistory, RouterProvider } from '@tanstack/react-router';
        import { expect, test } from 'vitest';
        import { App } from './App';
        import { createAppRouter } from './router';
        import { theme } from './theme';

        {{ResolveColour}}

        test("the navigation links take the theme's primary colour", async () => {
          render(
            <ThemeProvider theme={theme}>
              <RouterProvider router={createAppRouter(createMemoryHistory(), <App />)} />
            </ThemeProvider>,
          );

          const style = getComputedStyle(await screen.findByRole('link', { name: 'Home' }));
          // #0b6e4f, as the browser serialises it.
          expect(resolve(style, style.color)).toBe('rgb(11, 110, 79)');
        });
        """;

    private const string OtherLibraries = """
        import { Button as AntButton } from 'antd';
        import { Button as MantineButton } from '@mantine/core';
        import { Slot } from '@radix-ui/react-slot';

        export const others = [AntButton, MantineButton, Slot];
        """;

    [Fact]
    public void A_react_project_with_mui_and_the_library_theme_generates_from_the_real_packs_alone()
    {
        var files = Files(React, Mui);

        var package = files["web/app/package.json"];
        package.ShouldContain("\"@mui/material\": \"9.5.0\"");
        package.ShouldContain("\"@emotion/react\": \"11.14.0\"");
        package.ShouldContain("\"@emotion/styled\": \"11.14.1\"");
        foreach (var tailwind in new[] { "tailwindcss", "tailwind-merge", "tw-animate-css", "class-variance-authority", "@radix-ui/react-slot", "clsx" })
        {
            package.ShouldNotContain(tailwind);
        }

        files.Keys.ShouldNotContain("web/app/components.json");
        files.Keys.ShouldNotContain("web/app/src/lib/utils.ts");
        files.Keys.Where(path => path.StartsWith("web/app/src/components/ui/button", StringComparison.Ordinal)).ShouldBeEmpty();

        var theme = files["web/app/src/theme.ts"];
        theme.ShouldContain("import { createTheme } from '@mui/material/styles';");
        theme.ShouldContain("export const theme = createTheme({");
        files.Keys.ShouldContain("web/app/src/theme.test.tsx");

        var main = files["web/app/src/main.tsx"];
        main.ShouldContain("import { theme } from './theme';");
        main.ShouldContain("<ThemeProvider theme={theme}>");
        main.ShouldContain("<CssBaseline />");
        main.IndexOf("<ThemeProvider", StringComparison.Ordinal).ShouldBeLessThan(main.IndexOf("<CssBaseline", StringComparison.Ordinal), "the baseline takes the theme's background and font");

        files["web/app/vite.config.ts"].ShouldNotContain("tailwind");
        files["web/app/src/index.css"].ShouldNotContain("tailwind");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use MUI (@mui/material) and the theme in src/theme.ts (docs/adr).'");

        var agents = files["AGENTS.md"];
        agents.ShouldContain("## UI components: MUI");
        agents.ShouldContain("## Styling: the library's theme");
        agents.ShouldNotContain("shadcn");
        agents.ShouldNotContain("Tailwind");
        agents.ShouldNotContain("src/index.css` theme variables");
        Adr(files, "frontend-ui-library-mui").ShouldContain("# UI components: MUI");
        Adr(files, "frontend-styling-library-theme").ShouldContain("`src/theme.ts`");
        Adr(files, "frontend-design-system-none").ShouldNotContain("Tailwind");
    }

    public static TheoryData<string, string[]> MuiProjects => new()
    {
        { WithAgents, Mui },
        { WithAgents, MuiOtherChoices },
        { React, MuiRouterAndMsal },
    };

    [Theory]
    [MemberData(nameof(MuiProjects))]
    public void With_mui_no_react_source_carries_a_class_name_or_a_shadcn_component(string profile, string[] overrides)
    {
        var files = Files(profile, overrides);

        var sources = WebSources(files).ToList();
        sources.ShouldNotBeEmpty();
        foreach (var (path, content) in sources)
        {
            content.ShouldNotContain("className", customMessage: path);
            content.ShouldNotContain("@/components/ui/button", customMessage: path);
            content.ShouldNotContain("@/lib/utils", customMessage: path);
        }
    }

    [Theory]
    [InlineData("web/app/src/agents/AgentPanel.tsx")]
    [InlineData("web/app/src/dashboards/DashboardAssistant.tsx")]
    [InlineData("web/app/src/dashboards/DashboardCanvas.tsx")]
    [InlineData("web/app/src/dashboards/DashboardEditor.tsx")]
    [InlineData("web/app/src/dashboards/Failure.tsx")]
    [InlineData("web/app/src/dashboards/Pager.tsx")]
    [InlineData("web/app/src/dashboards/PendingWidget.tsx")]
    [InlineData("web/app/src/dashboards/PlacedWidget.tsx")]
    public void With_mui_the_dashboards_and_the_agent_panel_are_built_from_mui_components(string screen)
    {
        foreach (var overrides in new[] { Mui, MuiOtherChoices })
        {
            var content = Files(WithAgents, overrides)[screen];

            content.ShouldNotStartWith("throw new Error", customMessage: screen);
            content.ShouldMatch(@"import \{[^}]*\bButton\b[^}]*\} from '@mui/material';", customMessage: screen);
        }
    }

    [Fact]
    public void With_mui_and_the_library_theme_ag_grid_takes_its_colours_from_the_mui_theme()
    {
        var files = Files(WithAgents, MuiOtherChoices);

        var columns = files["web/app/src/components/ui/data-table-columns.ts"];
        columns.ShouldContain("import { theme as appTheme } from '@/theme';");
        columns.ShouldContain("""
            export const theme = themeQuartz.withParams({
              fontFamily: appTheme.typography.fontFamily,
              backgroundColor: appTheme.palette.background.paper,
              foregroundColor: appTheme.palette.text.primary,
              borderColor: appTheme.palette.divider,
              headerBackgroundColor: appTheme.palette.action.hover,
              accentColor: appTheme.palette.primary.main,
            });
            """);
        columns.ShouldNotContain("var(--");
        files["AGENTS.md"].ShouldContain("The grid's colours, borders and font come from the MUI theme in `src/theme.ts`");
        Adr(files, "frontend-data-grid-ag-grid").ShouldContain("Quartz theme bound to the MUI theme in `src/theme.ts`");
    }

    [Fact]
    public void The_library_theme_for_a_library_it_does_not_build_yet_fails_the_web_tests_saying_so()
    {
        using var project = new GeneratedProject("saas-standard-vue", Mui[1], "frontend.ui-library=vuetify");

        var theme = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "theme.ts"));
        theme.ShouldStartWith("throw new Error('The library-theme pack builds the theme for MUI only so far; frontend.ui-library is vuetify (docs/adr/0006 of the generator).');");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "theme.test.ts")).ShouldContain("import './theme';");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "main.ts")).ShouldNotContain("ThemeProvider");
    }

    [Fact]
    public void The_mui_web_app_with_dashboards_and_the_agent_panel_builds_lints_is_formatted_and_passes_its_tests_and_lint_refuses_other_libraries()
    {
        using var project = new GeneratedProject(WithAgents, Mui);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/theme.test.tsx > MUI's components take their colours from the theme", customMessage: testOutput);
        plain.ShouldContain("✓ src/theme.test.tsx > no component writes a colour of its own", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.tsx", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.tsx", customMessage: testOutput);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.tsx", customMessage: testOutput);
        FormatIsClean(project);

        project.Write("web/app/src/pages/OtherLibraries.ts", OtherLibraries);
        var (exitCode, lintOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, lintOutput);
        foreach (var library in new[] { "antd", "@mantine/core", "@radix-ui/react-slot" })
        {
            lintOutput.ShouldContain($"'{library}' import is restricted from being used by a pattern. Use MUI (@mui/material) and the theme in src/theme.ts (docs/adr)");
        }
    }

    [Fact]
    public void The_mui_web_app_with_other_forms_validation_grid_and_charts_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(WithAgents, MuiOtherChoices);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/components/ui/data-table.test.tsx", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.tsx", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.tsx", customMessage: testOutput);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.tsx", customMessage: testOutput);
        FormatIsClean(project);
    }

    [Fact]
    public void The_mui_web_app_with_tanstack_router_and_msal_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(React, MuiRouterAndMsal);

        var testOutput = InstallBuildLintAndTest(project);

        Escapes().Replace(testOutput, string.Empty).ShouldContain("✓ src/theme.test.tsx", customMessage: testOutput);
        FormatIsClean(project);
    }

    [Fact]
    public void A_component_that_writes_a_colour_of_its_own_fails_the_theme_check_naming_it()
    {
        using var project = new GeneratedProject(React, Mui);
        RunWeb(project, "install");
        project.Write("web/app/src/pages/Alarm.tsx", OwnColour);

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "test", "src/theme.test.tsx");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("pages/Alarm.tsx");
    }

    [Theory]
    [InlineData("frontend.routing=react-router")]
    [InlineData("frontend.routing=tanstack-router")]
    public void With_mui_the_navigation_links_take_the_theme_s_primary_colour(string routing)
    {
        using var project = new GeneratedProject(React, [.. Mui, routing]);
        RunWeb(project, "install");
        var themePath = Path.Combine(project.ProjectDirectory, "web", "app", "src", "theme.ts");
        var theme = File.ReadAllText(themePath);
        theme.ShouldContain("primary: { main: '#2563eb' }");
        File.WriteAllText(themePath, theme.Replace("primary: { main: '#2563eb' }", "primary: { main: '#0b6e4f' }", StringComparison.Ordinal));
        project.Write("web/app/src/links.test.tsx", routing.EndsWith("tanstack-router", StringComparison.Ordinal) ? TanStackLinks : ReactRouterLinks);

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "test", "src/links.test.tsx");

        exitCode.ShouldBe(0, output);
    }

    private static void FormatIsClean(GeneratedProject project)
    {
        var (exitCode, format) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, format);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static string Adr(Dictionary<string, string> files, string option) =>
        files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith($"-{option}.md", StringComparison.Ordinal)).Value;

    private static IEnumerable<KeyValuePair<string, string>> WebSources(Dictionary<string, string> files) =>
        files.Where(file => file.Key.StartsWith("web/app/src/", StringComparison.Ordinal) && Regex.IsMatch(file.Key, @"\.(ts|tsx)$"));

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
}

using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.design-system=tokens (roadmap M3-57): a Style Dictionary token build in web/packages/tokens, the one place
/// the project's colours, radii and spacing live, compiled into the styling option's form. With Tailwind the app reads
/// them as theme variables (bg-primary, rounded-md, p-4); with CSS Modules as CSS custom properties (var(--ds-...)).
/// React and Vue alike: there is no component package.
/// </summary>
public sealed class DesignTokensTests
{
    private const string Choice = "frontend.design-system=tokens";
    private const string LibraryTheme = "frontend.styling=library-theme";

    /// <summary>The theme takes the primary colour and the spacing steps from their tokens, changed in the test.</summary>
    private const string ThemeTokensTest = """
        import { expect, test } from 'vitest';
        import { theme } from './theme';

        test('the theme takes its primary colour from the tokens', () => {
          expect(theme.palette.primary.main).toBe('#0b6e4f');
        });

        test('the theme takes its spacing steps from the tokens', () => {
          expect(theme.spacing(1)).toBe('0.3rem');
          expect(theme.spacing(4)).toBe('1.25rem');
        });

        test('a negative step takes its token with the sign, and a string passes through', () => {
          expect(theme.spacing(-4)).toBe('calc(-1 * 1.25rem)');
          expect(theme.spacing(1, 'auto')).toBe('0.3rem auto');
        });

        """;

    private const string ReactScreen = """
        export function Tokens() {
          return <section className="bg-primary text-primary-foreground rounded-md p-4">Tokens</section>;
        }
        """;

    private const string VueScreen = """
        <template>
          <section class="bg-primary text-primary-foreground rounded-md p-4">Tokens</section>
        </template>
        """;

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    public void A_project_with_tokens_generates_from_the_real_packs_alone_without_a_component_package(string profile)
    {
        var files = Files(profile, Choice);

        files.Keys.ShouldContain("web/packages/tokens/package.json");
        files.Keys.ShouldContain("web/packages/tokens/src/spacing.tokens.json");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/packages/ui/", StringComparison.Ordinal));
        files["web/pnpm-workspace.yaml"].ShouldContain("  - packages/tokens");
        files["web/app/package.json"].ShouldContain("\"@shop/tokens\": \"workspace:*\"");
        files["web/app/package.json"].ShouldContain("pnpm --filter @shop/tokens build");
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    public void With_tailwind_the_tokens_theme_is_imported_after_the_ui_librarys_theme_so_the_tokens_win(string profile)
    {
        var files = Files(profile, Choice);

        var styles = files["web/app/src/index.css"];
        var tokens = styles.IndexOf("@import '@shop/tokens/theme.css';", StringComparison.Ordinal);
        tokens.ShouldBeGreaterThan(styles.LastIndexOf("@theme inline", StringComparison.Ordinal), styles);
        styles.ShouldNotContain("tokens.css");
        files["AGENTS.md"].ShouldContain("is changed in its token, not in `:root`");
    }

    [Theory]
    [InlineData("saas-standard-react", "frontend.ui-library=radix-headless")]
    [InlineData("saas-standard-vue", "frontend.ui-library=reka-ui")]
    public void With_css_modules_the_tokens_custom_properties_are_imported_before_any_rule(string profile, string library)
    {
        using var project = new GeneratedProject(profile, Choice, "frontend.styling=css-modules", library);

        var styles = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "index.css"));
        var statements = styles.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("/*", StringComparison.Ordinal) && !line.StartsWith('*'));
        statements.FirstOrDefault().ShouldBe("@import '@shop/tokens/tokens.css';", styles);
        styles.ShouldNotContain("theme.css");
    }

    [Theory]
    [InlineData("saas-standard-react", "web/app/src/pages/Tokens.tsx", ReactScreen)]
    [InlineData("saas-standard-vue", "web/app/src/pages/TokensPage.vue", VueScreen)]
    public void The_app_styles_its_colours_radii_and_spacing_with_the_tokens_through_tailwinds_theme(string profile, string screen, string source)
    {
        using var project = new GeneratedProject(profile, Choice);
        project.AddModule("billing");
        project.Write(screen, source + "\n");

        RunWeb(project, "install", "build", "lint", "test");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
        var css = AppCss(project);
        css.ShouldContain("--ds-color-primary:");
        css.ShouldContain(".bg-primary{background-color:var(--ds-color-primary)}");
        css.ShouldContain(".text-primary-foreground{color:var(--ds-color-primary-foreground)}");
        css.ShouldContain(".rounded-md{border-radius:var(--ds-radius-md)}");
        css.ShouldContain(".p-4{padding:var(--ds-spacing-4)}");
    }

    [Fact]
    public void With_css_modules_the_app_carries_the_tokens_as_css_custom_properties()
    {
        using var project = new GeneratedProject("saas-standard-react", Choice, "frontend.styling=css-modules", "frontend.ui-library=radix-headless");

        RunWeb(project, "install", "build");

        File.Exists(Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "dist", "theme.css")).ShouldBeFalse("no Tailwind, no Tailwind theme");
        var css = AppCss(project);
        css.ShouldContain("--ds-color-primary:");
        css.ShouldContain("--ds-radius-md:");
        css.ShouldContain("--ds-spacing-4:");
    }

    [Fact]
    public void A_token_that_references_a_token_that_does_not_exist_fails_the_build()
    {
        using var project = new GeneratedProject("saas-standard-vue", Choice);
        project.Write("web/packages/tokens/src/broken.tokens.json", """
            {
              "color": {
                "broken": { "$type": "color", "$value": "{color.missing}" }
              }
            }
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("could not be found");
    }

    [Theory]
    [InlineData("saas-standard-react", "frontend.ui-library=mui")]
    [InlineData("saas-standard-vue", "frontend.ui-library=vuetify")]
    public void With_the_library_theme_the_tokens_also_compile_to_a_typescript_module_for_the_theme(string profile, string library)
    {
        // Vuetify has no pack yet, so the project is generated with stand-ins for it.
        using var project = new GeneratedProject(profile, Choice, LibraryTheme, library);
        string Read(string path) => File.ReadAllText(Path.Combine(project.ProjectDirectory, path));

        var build = Read("web/packages/tokens/build.mjs");
        build.ShouldNotContain("throw new Error");
        build.ShouldContain("{ destination: 'tokens.ts', format: 'typescript/tokens' },");
        Read("web/packages/tokens/package.json").ShouldContain("\".\": \"./dist/tokens.ts\"");
        Read("web/app/src/index.css").ShouldNotContain("@shop/tokens", customMessage: "the theme reads the tokens, not the stylesheet");
        Read("AGENTS.md").ShouldContain("through the library's theme in `src/theme.ts`");
    }

    [Fact]
    public void With_mui_the_theme_is_built_from_the_tokens_instead_of_colours_of_its_own()
    {
        var theme = Files("saas-standard-react", Choice, LibraryTheme, "frontend.ui-library=mui")["web/app/src/theme.ts"];

        theme.ShouldContain("import { tokens } from '@shop/tokens';");
        theme.ShouldContain("primary: { main: tokens.color.primary, contrastText: tokens.color['primary-foreground'] },");
        theme.ShouldContain("secondary: { main: tokens.color.accent, contrastText: tokens.color['accent-foreground'] },");
        theme.ShouldNotContain("#", customMessage: "a colour of the theme's own");
    }

    [Fact]
    public void The_mui_web_app_on_the_tokens_builds_lints_is_formatted_and_its_theme_takes_changed_tokens()
    {
        using var project = new GeneratedProject("saas-standard-react", Choice, LibraryTheme, "frontend.ui-library=mui");
        var colours = Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "src", "color.tokens.json");
        File.WriteAllText(colours, File.ReadAllText(colours).Replace("\"#3730a3\"", "\"#0b6e4f\"", StringComparison.Ordinal));
        var spacing = Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "src", "spacing.tokens.json");
        File.WriteAllText(spacing, File.ReadAllText(spacing)
            .Replace("\"1\": { \"$value\": { \"value\": 0.25,", "\"1\": { \"$value\": { \"value\": 0.3,", StringComparison.Ordinal)
            .Replace("\"4\": { \"$value\": { \"value\": 1,", "\"4\": { \"$value\": { \"value\": 1.25,", StringComparison.Ordinal));
        project.Write("web/app/src/theme-tokens.test.ts", ThemeTokensTest);

        RunWeb(project, "install", "build", "lint", "test");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
        File.Exists(Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "dist", "tokens.ts")).ShouldBeTrue();
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static string AppCss(GeneratedProject project)
    {
        var assets = Path.Combine(project.ProjectDirectory, "web", "app", "dist", "assets");
        return string.Concat(Directory.GetFiles(assets, "*.css").Select(File.ReadAllText));
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

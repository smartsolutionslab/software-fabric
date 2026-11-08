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
    public void Tokens_for_a_styling_the_pack_does_not_compile_to_fail_loudly(string profile, string library)
    {
        using var project = new GeneratedProject(profile, Choice, "frontend.styling=library-theme", library);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "build.mjs"))
            .ShouldStartWith("throw new Error('The tokens pack compiles tokens for Tailwind and CSS Modules only so far; frontend.styling is library-theme (docs/adr/0006 of the generator).');");
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

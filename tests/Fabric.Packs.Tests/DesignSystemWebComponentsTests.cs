using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.design-system=web-components (roadmap M3-58): the tokens package of frontend.design-system=tokens, unchanged,
/// plus the design system's own components as Lit web components with Storybook in web/packages/ui. The components
/// render in shadow DOM and are styled only through the tokens' --ds-* CSS variables, so React and Vue use the same
/// elements; Vue is told to leave the ds- tags to the browser.
/// </summary>
public sealed class DesignSystemWebComponentsTests
{
    private const string Choice = "frontend.design-system=web-components";
    private const string LibraryTheme = "frontend.styling=library-theme";

    /// <summary>With MUI a screen takes no class names; the theme styles the page around the element.</summary>
    private const string MuiScreen = """
        export function Tokens() {
          return (
            <section>
              <ds-badge tone="primary">New</ds-badge>
            </section>
          );
        }
        """;

    private const string ReactScreen = """
        export function Tokens() {
          return (
            <section className="bg-primary text-primary-foreground rounded-md p-4">
              <ds-badge tone="primary">New</ds-badge>
            </section>
          );
        }
        """;

    private const string ReactScreenTest = """
        import { render } from '@testing-library/react';
        import { expect, test } from 'vitest';
        import { Tokens } from './Tokens';

        test('the screen shows the design system badge as a web component', () => {
          const { container } = render(<Tokens />);

          const badge = container.querySelector('ds-badge');
          expect(badge?.shadowRoot).not.toBeNull();
          expect(badge).toHaveTextContent('New');
        });
        """;

    private const string VueScreen = """
        <template>
          <section class="bg-primary text-primary-foreground rounded-md p-4">
            <ds-badge tone="primary">New</ds-badge>
          </section>
        </template>
        """;

    private const string VueScreenTest = """
        import { render } from '@testing-library/vue';
        import { expect, test, vi } from 'vitest';
        import TokensPage from './TokensPage.vue';

        test('the screen shows the design system badge as a web component', () => {
          const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

          const { container } = render(TokensPage);

          const badge = container.querySelector('ds-badge');
          expect(badge?.shadowRoot).not.toBeNull();
          expect(badge).toHaveTextContent('New');
          expect(warn).not.toHaveBeenCalledWith(expect.stringContaining('Failed to resolve component'));
        });
        """;

    [Theory]
    [InlineData("saas-standard-react", "web/app/src/main.tsx")]
    [InlineData("saas-standard-vue", "web/app/src/main.ts")]
    public void A_project_with_web_components_generates_from_the_real_packs_alone(string profile, string main)
    {
        var files = Files(profile, Choice);

        files.Keys.ShouldContain("web/packages/tokens/package.json");
        files.Keys.ShouldContain("web/packages/ui/package.json");
        files.Keys.ShouldContain("web/packages/ui/src/Badge.ts");
        files["web/packages/ui/package.json"].ShouldContain("\"lit\": \"3.3.3\"");
        files["web/packages/ui/package.json"].ShouldContain("\"@storybook/web-components-vite\"");
        files["web/pnpm-workspace.yaml"].ShouldContain("  - packages/tokens\n  - packages/ui");
        files["web/app/package.json"].ShouldContain("\"@shop/tokens\": \"workspace:*\"");
        files["web/app/package.json"].ShouldContain("\"@shop/ui\": \"workspace:*\"");
        files["web/app/package.json"].ShouldContain("pnpm --filter @shop/tokens build");
        files[main].ShouldContain("import '@shop/ui';");
        files["web/app/src/test-setup.ts"].ShouldContain("import '@shop/ui';");
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("saas-standard-vue", "frontend.styling=css-modules", "frontend.ui-library=reka-ui")]
    public void The_tokens_package_and_the_apps_stylesheet_are_those_of_the_tokens_option(string profile, params string[] overrides)
    {
        var tokens = Files(profile, ["frontend.design-system=tokens", .. overrides]);
        var webComponents = Files(profile, [Choice, .. overrides]);

        var package = webComponents.Where(file => file.Key.StartsWith("web/packages/tokens/", StringComparison.Ordinal)).ToDictionary();
        package.ShouldNotBeEmpty();
        package.ShouldBe(tokens.Where(file => file.Key.StartsWith("web/packages/tokens/", StringComparison.Ordinal)).ToDictionary());
        webComponents["web/app/src/index.css"].ShouldBe(tokens["web/app/src/index.css"]);
    }

    [Fact]
    public void Vue_leaves_the_design_systems_tags_to_the_browser()
    {
        var files = Files("saas-standard-vue", Choice);

        files["web/app/vite.config.ts"].ShouldContain("const customElementPrefixes = ['ds-'];");
        files["web/app/vite.config.ts"].ShouldContain("vue({ template: { compilerOptions: { isCustomElement } } }),");
    }

    [Fact]
    public void React_type_checks_the_design_systems_tags_as_jsx_elements()
    {
        var react = Files("saas-standard-react", Choice);
        var vue = Files("saas-standard-vue", Choice);

        react["web/app/src/design-system.d.ts"].ShouldContain("'ds-badge':");
        vue.Keys.ShouldNotContain("web/app/src/design-system.d.ts");
    }

    [Theory]
    [InlineData("saas-standard-react", "web/app/src/pages/Tokens.tsx", ReactScreen, "web/app/src/pages/Tokens.test.tsx", ReactScreenTest)]
    [InlineData("saas-standard-vue", "web/app/src/pages/TokensPage.vue", VueScreen, "web/app/src/pages/TokensPage.test.ts", VueScreenTest)]
    public void The_workspace_with_a_screen_using_a_component_installs_builds_lints_passes_its_tests_and_its_format_check(
        string profile,
        string screen,
        string source,
        string screenTest,
        string testSource)
    {
        using var project = new GeneratedProject(profile, Choice);
        project.AddModule("billing");
        project.Write(screen, source + "\n");
        project.Write(screenTest, testSource + "\n");

        RunWeb(project, "install", "build", "lint", "test");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
        File.Exists(Path.Combine(project.ProjectDirectory, "web", "packages", "ui", "storybook-static", "index.html")).ShouldBeTrue("the ui package's build builds Storybook");
        var assets = Path.Combine(project.ProjectDirectory, "web", "app", "dist", "assets");
        var script = string.Concat(Directory.GetFiles(assets, "*.js").Select(File.ReadAllText));
        script.ShouldContain("ds-badge", Case.Sensitive, "the app registers the design system's elements");
        script.ShouldContain("var(--ds-color-primary)", Case.Sensitive, "the badge's shadow styles read the tokens");
        var css = string.Concat(Directory.GetFiles(assets, "*.css").Select(File.ReadAllText));
        css.ShouldContain("--ds-color-primary:");
    }

    [Fact]
    public void A_story_that_does_not_type_check_fails_the_build()
    {
        using var project = new GeneratedProject("saas-standard-vue", Choice);
        project.Write("web/packages/ui/src/Broken.stories.ts", """
            import type { Meta, StoryObj } from '@storybook/web-components-vite';
            import { html } from 'lit';
            import type { BadgeTone } from './Badge';

            type BadgeArgs = { tone: BadgeTone; label: string };

            const meta = {
              args: { tone: 'accent', label: 'New' },
              render: ({ tone, label }) => html`<ds-badge tone=${tone}>${label}</ds-badge>`,
            } satisfies Meta<BadgeArgs>;

            export default meta;

            export const Broken: StoryObj<BadgeArgs> = { args: { tone: 'shouting' } };
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Broken.stories.ts");
    }

    [Theory]
    [InlineData("background-color: var(--ds-color-primary);", "background-color: #3730a3;")]
    [InlineData("background-color: var(--ds-color-accent);", "background-color: red;")]
    [InlineData("background-color: var(--ds-color-accent);", "background-color: transparent;")]
    [InlineData("background-color: var(--ds-color-accent);", "background-color: var(--brand-red);")]
    [InlineData("background-color: var(--ds-color-accent);", "background: red;")]
    [InlineData("background-color: var(--ds-color-accent);", "border: 1px solid red;")]
    [InlineData("background-color: var(--ds-color-accent);", "outline: 2px solid red;")]
    [InlineData("background-color: var(--ds-color-accent);", "box-shadow: 0 0 4px red;")]
    // The last declaration of a rule needs no semicolon.
    [InlineData("color: var(--ds-color-accent-foreground);", "color: red")]
    // A colour equal to the browser's default is still a colour of its own.
    [InlineData("display: inline-flex;", "display: inline-flex; fill: black;")]
    // A state's colour counts as well, though no test hovers the badge.
    [InlineData(".primary {", ".accent:hover { color: red; } .primary {")]
    public void A_component_style_with_a_value_of_its_own_fails_the_ui_packages_tests(string declaration, string mutation)
    {
        using var project = new GeneratedProject("saas-standard-react", Choice);
        var badge = Path.Combine(project.ProjectDirectory, "web", "packages", "ui", "src", "Badge.ts");
        var source = File.ReadAllText(badge);
        source.Split(declaration).Length.ShouldBe(2, $"Badge.ts declares {declaration} once");
        File.WriteAllText(badge, source.Replace(declaration, mutation, StringComparison.Ordinal));
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "--filter", "@shop/ui", "test");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("a badge is styled only through the design tokens");
    }

    [Theory]
    [InlineData("saas-standard-react", "frontend.ui-library=mui")]
    [InlineData("saas-standard-vue", "frontend.ui-library=vuetify")]
    public void With_the_library_theme_the_tokens_package_is_the_tokens_options_and_the_app_imports_its_variables(string profile, string library)
    {
        // Vuetify has no pack yet, so the projects are generated with stand-ins for it.
        using var tokens = new GeneratedProject(profile, "frontend.design-system=tokens", LibraryTheme, library);
        using var webComponents = new GeneratedProject(profile, Choice, LibraryTheme, library);

        var package = TokensPackage(webComponents);
        package.ShouldNotBeEmpty();
        package.ShouldBe(TokensPackage(tokens));
        File.ReadAllText(Path.Combine(webComponents.ProjectDirectory, "web", "app", "src", "index.css")).ShouldContain("@import '@shop/tokens/tokens.css';", customMessage: "the components' shadow styles read the --ds-* variables");
    }

    [Fact]
    public void The_mui_workspace_with_a_screen_using_a_component_installs_builds_lints_passes_its_tests_and_its_format_check()
    {
        using var project = new GeneratedProject("saas-standard-react", Choice, LibraryTheme, "frontend.ui-library=mui");
        project.Write("web/app/src/pages/Tokens.tsx", MuiScreen + "\n");
        project.Write("web/app/src/pages/Tokens.test.tsx", ReactScreenTest + "\n");

        RunWeb(project, "install", "build", "lint", "test");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
        var assets = Path.Combine(project.ProjectDirectory, "web", "app", "dist", "assets");
        var css = string.Concat(Directory.GetFiles(assets, "*.css").Select(File.ReadAllText));
        css.ShouldContain("--ds-color-primary:");
    }

    private static Dictionary<string, string> TokensPackage(GeneratedProject project)
    {
        var package = Path.Combine(project.ProjectDirectory, "web", "packages", "tokens");
        return Directory.GetFiles(package, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(package, path), File.ReadAllText);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
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

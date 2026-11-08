using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.design-system=tokens-and-components on saas-standard-react: a Style Dictionary token build in
/// web/packages/tokens, the design system's own components with Storybook in web/packages/ui, and the app styling with
/// the tokens through Tailwind's @theme.
/// </summary>
public sealed class DesignSystemTests
{
    private const string Profile = "saas-standard-react";

    private const string Choice = "frontend.design-system=tokens-and-components";

    /// <summary>A screen a developer would write: a token's utility in the app, and a component of the design system.</summary>
    private const string TokenScreen = """
        import { Badge } from '@shop/ui';

        export function Tokens() {
          return (
            <section className="bg-primary text-primary-foreground rounded-md">
              <Badge>New</Badge>
            </section>
          );
        }
        """;

    [Fact]
    public void A_project_with_tokens_and_components_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Choice]);

        generated.Errors.ShouldBeEmpty();
        generated.Files.Select(file => file.Path).ShouldContain("web/packages/tokens/package.json");
        generated.Files.Select(file => file.Path).ShouldContain("web/packages/ui/package.json");
    }

    [Fact]
    public void The_workspace_with_the_design_system_installs_builds_lints_passes_its_tests_and_its_format_check()
    {
        using var project = new GeneratedProject(Profile, Choice);
        project.AddModule("billing");

        RunWeb(project, "install", "build", "lint", "test");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
        File.Exists(Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "dist", "tokens.css")).ShouldBeTrue("the tokens build writes dist/tokens.css");
        File.Exists(Path.Combine(project.ProjectDirectory, "web", "packages", "ui", "storybook-static", "index.html")).ShouldBeTrue("the ui package's build builds Storybook");
    }

    [Fact]
    public void The_app_styles_with_the_tokens_through_tailwinds_theme_and_its_components_from_the_ui_package()
    {
        using var project = new GeneratedProject(Profile, Choice);
        project.Write("web/app/src/pages/Tokens.tsx", TokenScreen);

        RunWeb(project, "install", "build");

        var tokens = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "dist", "tokens.css"));
        tokens.ShouldContain("--ds-color-primary:");
        var assets = Path.Combine(project.ProjectDirectory, "web", "app", "dist", "assets");
        var css = string.Concat(Directory.GetFiles(assets, "*.css").Select(File.ReadAllText));
        css.ShouldContain("--ds-color-primary:");
        css.ShouldContain(".bg-primary{background-color:var(--ds-color-primary)}");
        css.ShouldContain(".text-primary-foreground{color:var(--ds-color-primary-foreground)}");

        // Badge's own utilities, which only the ui package's source names.
        css.ShouldContain(".bg-accent{background-color:var(--ds-color-accent)}");
    }

    [Fact]
    public void A_token_that_references_a_token_that_does_not_exist_fails_the_build()
    {
        using var project = new GeneratedProject(Profile, Choice);
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

    [Fact]
    public void A_story_that_does_not_type_check_fails_the_build()
    {
        using var project = new GeneratedProject(Profile, Choice);
        project.Write("web/packages/ui/src/Broken.stories.tsx", """
            import type { Meta, StoryObj } from '@storybook/react-vite';
            import { Badge } from './Badge';

            const meta = { component: Badge } satisfies Meta<typeof Badge>;
            export default meta;

            export const Broken: StoryObj<typeof meta> = { args: { children: 'New', tone: 'shouting' } };
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Broken.stories.tsx");
    }

    [Fact]
    public void Each_test_in_the_ui_package_starts_from_an_empty_document()
    {
        using var project = new GeneratedProject(Profile, Choice);
        project.Write("web/packages/ui/src/Badges.test.tsx", """
            import { render, screen } from '@testing-library/react';
            import { expect, test } from 'vitest';
            import { Badge } from './Badge';

            test('a first badge', () => {
              render(<Badge>New</Badge>);

              expect(screen.getByText('New')).toBeInTheDocument();
            });

            test('a second badge with the same label', () => {
              render(<Badge tone="primary">New</Badge>);

              expect(screen.getByText('New')).toHaveClass('bg-primary');
            });
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "packages", "ui"), "pnpm", "test");

        exitCode.ShouldBe(0, $"pnpm test in web/packages/ui:\n{output}");
    }

    [Fact]
    public void The_ui_packages_tests_pass_on_a_fresh_clone_without_a_prior_build()
    {
        using var project = new GeneratedProject(Profile, Choice);
        RunWeb(project, "install");
        File.Exists(Path.Combine(project.ProjectDirectory, "web", "packages", "tokens", "dist", "theme.css")).ShouldBeFalse("a fresh clone has no built tokens");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "--filter", "@shop/ui", "test");

        exitCode.ShouldBe(0, $"pnpm --filter @shop/ui test:\n{output}");
    }

    [Fact]
    public void A_design_system_for_another_styling_than_tailwind_fails_loudly()
    {
        using var project = new GeneratedProject(Profile, Choice, "frontend.styling=css-modules", "frontend.ui-library=radix-headless");
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("The tokens-and-components pack builds React components styled with Tailwind only so far");
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

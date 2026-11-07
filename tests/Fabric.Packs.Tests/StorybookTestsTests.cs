using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.test.extras=storybook-tests on saas-standard-react with tokens-and-components: the ui package's tests render
/// every story of the design system and run its play function, so a story that breaks fails pnpm test.
/// </summary>
public sealed class StorybookTestsTests
{
    private const string Profile = "saas-standard-react";

    private static readonly string[] Choices = ["frontend.design-system=tokens-and-components", "frontend.test.extras=accessibility,storybook-tests"];

    [Fact]
    public void A_project_with_storybook_tests_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, Choices);

        generated.Errors.ShouldBeEmpty();
        generated.Files.Select(file => file.Path).ShouldContain("web/packages/ui/src/stories.test.tsx");
    }

    [Fact]
    public void The_workspace_with_storybook_tests_installs_builds_lints_and_tests_every_story()
    {
        using var project = new GeneratedProject(Profile, Choices);
        project.AddModule("billing");
        RunWeb(project, "install", "build", "lint");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "packages", "ui"), "pnpm", "exec", "vitest", "run", "--reporter", "verbose");

        exitCode.ShouldBe(0, $"vitest in web/packages/ui:\n{output}");
        output.ShouldContain("Badge.stories.tsx > Accent");
        output.ShouldContain("Badge.stories.tsx > Primary");
        RunWeb(project, "test");
    }

    [Fact]
    public void A_story_that_throws_when_it_renders_fails_the_tests()
    {
        using var project = new GeneratedProject(Profile, Choices);
        project.Write("web/packages/ui/src/Broken.stories.tsx", """
            import type { Meta, StoryObj } from '@storybook/react-vite';
            import { Badge } from './Badge';

            const meta = { component: Badge, args: { children: 'New' } } satisfies Meta<typeof Badge>;
            export default meta;

            export const Throws: StoryObj<typeof meta> = {
              render: () => {
                throw new Error('this story cannot render');
              },
            };
            """);
        RunWeb(project, "install", "build");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "test");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Broken.stories.tsx > Throws");
        output.ShouldContain("this story cannot render");
    }

    [Fact]
    public void A_story_whose_play_function_fails_fails_the_tests()
    {
        using var project = new GeneratedProject(Profile, Choices);
        project.Write("web/packages/ui/src/Broken.stories.tsx", """
            import type { Meta, StoryObj } from '@storybook/react-vite';
            import { expect, within } from 'storybook/test';
            import { Badge } from './Badge';

            const meta = { component: Badge, args: { children: 'New' } } satisfies Meta<typeof Badge>;
            export default meta;

            export const ShowsAnotherLabel: StoryObj<typeof meta> = {
              play: async ({ canvasElement }) => {
                await expect(within(canvasElement).getByText('Old')).toBeInTheDocument();
              },
            };
            """);
        RunWeb(project, "install", "build");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "test");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Broken.stories.tsx > ShowsAnotherLabel");
        output.ShouldContain("Unable to find an element with the text: Old");
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

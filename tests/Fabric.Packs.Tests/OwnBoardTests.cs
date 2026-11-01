namespace Fabric.Packs.Tests;

/// <summary>This repository runs the board protocol it generates (roadmap M2-00): its copy may not drift from the packs.</summary>
public sealed class OwnBoardTests
{
    private static readonly string[] Copies =
    [
        ".github/board.env",
        ".github/review-findings.schema.json",
        ".github/workflows/agent-executor.yml",
        ".github/workflows/board-janitor.yml",
        ".github/workflows/board-return.yml",
        ".github/workflows/commit-messages.yml",
        ".github/workflows/cross-model-review.yml",
        ".claude/skills/next-issue/SKILL.md",
    ];

    [Fact]
    public void The_repository_runs_the_board_protocol_the_packs_generate()
    {
        using var project = new GeneratedProject("saas-standard-react");
        var scripts = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, ".github", "scripts", "board"))
            .Select(path => Path.GetRelativePath(project.ProjectDirectory, path).Replace('\\', '/'));

        var copiedScripts = Directory.EnumerateFiles(Path.Combine(Repository.Root, ".github", "scripts", "board"))
            .Select(path => ".github/scripts/board/" + Path.GetFileName(path));
        copiedScripts.Order().ShouldBe(scripts.Order(), "the board scripts here are not the ones the packs generate; remove or regenerate them.");

        foreach (var file in Copies.Concat(scripts))
        {
            var copy = Path.Combine(Repository.Root, file);
            File.Exists(copy).ShouldBeTrue($"{file} is missing from this repository; copy it from a generated project.");
            File.ReadAllText(copy).ShouldBe(
                File.ReadAllText(Path.Combine(project.ProjectDirectory, file)),
                $"{file} differs from what the packs generate; regenerate the copy.");
        }
    }
}

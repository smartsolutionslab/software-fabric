namespace Fabric.Packs.Tests;

/// <summary>
/// The verify phase base.fabric ships: between the engineer and the reviewer, an agent observes the change working end
/// to end, not only its tests green, and the pull request records what it observed.
/// </summary>
public sealed class VerifyPhaseTests
{
    private const string Profile = "lean-crud";

    [Fact]
    public void A_generated_project_has_a_verify_skill_that_asks_for_the_running_system_not_the_tests()
    {
        using var project = new GeneratedProject(Profile);
        var skill = Path.Combine(project.ProjectDirectory, ".claude", "skills", "verify", "SKILL.md");

        File.Exists(skill).ShouldBeTrue("base.fabric generates no verify skill.");
        var text = File.ReadAllText(skill);
        text.ShouldStartWith("---\nname: verify\n");
        text.ShouldContain("end to end");
        text.ShouldContain("## How it was verified");
    }

    [Fact]
    public void The_next_issue_skill_runs_verify_between_the_engineer_and_the_reviewer()
    {
        using var project = new GeneratedProject(Profile);
        var phases = File.ReadAllLines(Path.Combine(project.ProjectDirectory, ".claude", "skills", "next-issue", "SKILL.md"))
            .Single(line => line.Contains("architect → test-writer", StringComparison.Ordinal));

        phases.ShouldContain("the lane's engineer");
        phases.ShouldContain("verify");
        phases.IndexOf("verify", StringComparison.Ordinal).ShouldBeGreaterThan(phases.IndexOf("the lane's engineer", StringComparison.Ordinal), phases);
        phases.IndexOf("verify", StringComparison.Ordinal).ShouldBeLessThan(phases.IndexOf("the lane's reviewer", StringComparison.Ordinal), phases);
    }

    [Fact]
    public void The_pull_request_template_asks_for_the_verification_and_offers_both_red_first_sections()
    {
        using var project = new GeneratedProject(Profile);
        var template = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "pull_request_template.md"));

        var verified = template.IndexOf("## How it was verified", StringComparison.Ordinal);
        verified.ShouldBeGreaterThanOrEqualTo(0, template);
        template[verified..].ShouldContain("end to end");
        template.ShouldContain("## Tests observed failing");
        template.ShouldContain("## Tests observed passing before the change");
    }
}

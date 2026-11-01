namespace Fabric.Packs.Tests;

/// <summary>
/// The generated backend CI's format check: the command it runs, against a fresh project and against a broken one.
/// </summary>
public sealed class FormattingTests
{
    private const string StepName = "- name: Check formatting";

    /// <summary>
    /// Overrides that let a profile be generated while its frontend packs are not written yet. Only frontend dimensions
    /// are overridden, so the backend checked is the profile's own.
    /// </summary>
    internal static readonly Dictionary<string, string[]> FrontendStandIns = new()
    {
        ["saas-standard-vue"] =
        [
            "frontend.framework=react",
            "frontend.state=redux-toolkit",
            "frontend.api-client=rtk-query-codegen",
            "frontend.routing=react-router",
            "frontend.forms=react-hook-form",
            "frontend.ui-library=shadcn",
            "frontend.styling=tailwind",
        ],
        ["industrial-event-driven-mfe"] = ["frontend.state=redux-toolkit", "frontend.api-client=rtk-query-codegen"],
    };

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    [Fact]
    public void The_backend_job_checks_formatting_before_it_builds()
    {
        using var project = new GeneratedProject("lean-crud");
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));
        var backend = ci[ci.IndexOf("\n  backend:\n", StringComparison.Ordinal)..ci.IndexOf("\n  frontend:\n", StringComparison.Ordinal)];

        backend.ShouldContain(StepName);
        backend.IndexOf(StepName, StringComparison.Ordinal)
            .ShouldBeLessThan(backend.IndexOf("run: dotnet build build.proj", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_fresh_project_of_every_profile_with_a_module_passes_the_format_check(string profile)
    {
        using var project = new GeneratedProject(profile, FrontendStandIns.GetValueOrDefault(profile, []));
        project.AddModule("billing");

        var (exitCode, output) = RunFormatCheck(project);

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void A_formatting_violation_in_a_module_or_its_tests_fails_the_format_check()
    {
        using var project = new GeneratedProject("lean-crud");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Unformatted.cs", """
            namespace Shop.Modules.Billing.Domain;

            public static class Unformatted
            {
                    public const int Answer = 42;
            }
            """);
        project.Write("tests/Modules/Billing.Tests/UnformattedTests.cs", """
            namespace Shop.Modules.Billing.Tests;

            public static class UnformattedTests
            {
              public const int Answer = 42;
            }
            """);

        var (exitCode, output) = RunFormatCheck(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Unformatted.cs");
        output.ShouldContain("UnformattedTests.cs");
    }

    /// <summary>Runs the backend CI's "Check formatting" step, as written in the generated ci.yml.</summary>
    internal static (int ExitCode, string Output) RunFormatCheck(GeneratedProject project)
    {
        var lines = File.ReadAllLines(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));
        var step = Array.FindIndex(lines, line => line.Trim() == StepName);
        step.ShouldBeGreaterThanOrEqualTo(0, "the generated ci.yml has no \"Check formatting\" step");
        var run = lines[step + 1].Trim();
        run.ShouldStartWith("run: ");

        return Shell.Run(project.ProjectDirectory, "bash", "-c", run["run: ".Length..]);
    }
}

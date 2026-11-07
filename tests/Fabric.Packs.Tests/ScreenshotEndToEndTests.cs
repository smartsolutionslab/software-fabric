namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.test.extras: screenshot-tests in the running application: a project's first e2e run writes the start page's
/// baseline in Playwright's pinned container, the next run renders the same page identically and passes, and a changed
/// start page fails the generated e2e script.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ScreenshotEndToEndTests
{
    private const string NoBaselines = "No screenshot baselines are committed yet";

    [Fact]
    public void A_changed_start_page_fails_the_screenshot_tests_against_the_baseline_the_first_run_wrote()
    {
        using var project = new GeneratedProject("saas-standard-react", "frontend.test.extras=accessibility,screenshot-tests");
        project.AddModule("billing");
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        var baseline = Path.Combine(project.ProjectDirectory, "web", "app", "e2e", "__screenshots__", "start-page.screenshot.ts", "start-page.png");

        var (firstExitCode, first) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        firstExitCode.ShouldBe(0, first);
        first.ShouldContain(NoBaselines);
        File.Exists(baseline).ShouldBeTrue(first);

        var (secondExitCode, second) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        secondExitCode.ShouldBe(0, second);
        second.ShouldNotContain(NoBaselines);
        second.ShouldContain("1 passed");

        // The gate: a start page that renders differently from its baseline fails the e2e script.
        var home = Path.Combine(project.ProjectDirectory, "web", "app", "src", "pages", "Home.tsx");
        var page = File.ReadAllText(home);
        page.ShouldContain("<h1>shop</h1>");
        File.WriteAllText(home, page.Replace("<h1>shop</h1>", "<h1>shop</h1>\n      <p>A changed start page</p>", StringComparison.Ordinal));

        var (changedExitCode, changed) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        changedExitCode.ShouldNotBe(0, changed);
        changed.ShouldContain("toHaveScreenshot");
        changed.ShouldContain("The screenshot tests failed.");
    }
}

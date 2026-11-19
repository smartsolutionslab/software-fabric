namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.test.extras: screenshot-tests in the running application: a project's first e2e run writes the baselines of
/// the start page and of each module page in the light and the dark theme in Playwright's pinned container, the next
/// run renders the same pages identically and passes, and a changed page fails the generated e2e script with the
/// difference written as an image for CI to upload.
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

        var (firstExitCode, first) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        firstExitCode.ShouldBe(0, first);
        first.ShouldContain(NoBaselines);
        Baselines(project).ShouldBe(
            ["billing-page.screenshot.ts/billing-page-dark.png", "billing-page.screenshot.ts/billing-page-light.png", "start-page.screenshot.ts/start-page.png"],
            first);

        var (secondExitCode, second) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        secondExitCode.ShouldBe(0, second);
        second.ShouldNotContain(NoBaselines);
        second.ShouldContain("3 passed");

        // The gate: a start page that renders differently from its baseline fails the e2e script, and so does a module
        // page that changes only in the dark theme, while its light screenshot still passes.
        var home = Path.Combine(project.ProjectDirectory, "web", "app", "src", "pages", "Home.tsx");
        var page = File.ReadAllText(home);
        page.ShouldContain("<h1>shop</h1>");
        File.WriteAllText(home, page.Replace("<h1>shop</h1>", "<h1>shop</h1>\n      <p>A changed start page</p>", StringComparison.Ordinal));
        ChangeTheDarkTheme(project);

        var (changedExitCode, changed) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        changedExitCode.ShouldNotBe(0, changed);
        changed.ShouldContain("toHaveScreenshot");
        changed.ShouldContain("2 failed");
        changed.ShouldContain("1 passed");
        changed.ShouldContain("The screenshot tests failed.");
        Report(changed);
        Differences(project).ShouldBe(["billing-page-dark-diff.png", "start-page-diff.png"], changed);
    }

    [Fact]
    public void A_vue_module_page_that_changes_in_the_dark_theme_fails_against_the_baselines_the_first_run_wrote()
    {
        using var project = new GeneratedProject("saas-standard-vue", "frontend.test.extras=accessibility,screenshot-tests");
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

        var (firstExitCode, first) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        firstExitCode.ShouldBe(0, first);
        first.ShouldContain(NoBaselines);
        Baselines(project).ShouldBe(
            ["billing-page.screenshot.ts/billing-page-dark.png", "billing-page.screenshot.ts/billing-page-light.png", "start-page.screenshot.ts/start-page.png"],
            first);

        // The start page and the light module page render as they did, after the e2e tests created more samples.
        ChangeTheDarkTheme(project);

        var (changedExitCode, changed) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        changedExitCode.ShouldNotBe(0, changed);
        changed.ShouldContain("1 failed");
        changed.ShouldContain("2 passed");
        changed.ShouldContain("The screenshot tests failed.");
        Report(changed);
        Differences(project).ShouldBe(["billing-page-dark-diff.png"], changed);
    }

    /// <summary>
    /// Prints the comparison's failure to the run's log, where CI shows it although the test passes: the e2e script's
    /// own lines, without the AppHost's output it prints after them.
    /// </summary>
    private static void Report(string output) =>
        Console.WriteLine(output.Split("--- AppHost output", 2)[0]);

    /// <summary>The committed baselines, relative to <c>web/app/e2e/__screenshots__</c>, in ordinal order.</summary>
    private static string[] Baselines(GeneratedProject project)
    {
        var root = Path.Combine(project.ProjectDirectory, "web", "app", "e2e", "__screenshots__");
        return Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The difference images the last run wrote where CI's e2e job uploads them from, by name.</summary>
    private static string[] Differences(GeneratedProject project)
    {
        var root = Path.Combine(project.ProjectDirectory, "web", "app", "test-results", "screenshots");
        return Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*-diff.png", SearchOption.AllDirectories).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>A visual change only the dark theme shows: the module page's section heading on another background.</summary>
    private static void ChangeTheDarkTheme(GeneratedProject project)
    {
        var styles = Path.Combine(project.ProjectDirectory, "web", "app", "src", "index.css");
        File.AppendAllText(styles, "\n.dark h2 {\n  background: oklch(0.45 0.2 30);\n}\n");
    }
}

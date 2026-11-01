namespace Fabric.Packs.Tests;

public sealed class FrontendTests
{
    private const string Profile = "saas-standard-react";

    [Fact]
    public void The_generated_web_app_installs_builds_lints_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Profile);
        var web = Path.Combine(project.ProjectDirectory, "web");

        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }
    }

    [Fact]
    public void The_generated_web_app_passes_its_format_check_and_a_formatting_violation_fails_it()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");

        // What CI has on disk when it checks formatting: the generated client and the build output included.
        foreach (var step in new[] { "install", "build" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (cleanExitCode, cleanOutput) = Shell.Run(app, "pnpm", "format:check");
        cleanExitCode.ShouldBe(0, $"pnpm format:check on the untouched project:\n{cleanOutput}");

        project.Write("web/app/src/unformatted.ts", "export const greeting = \"hello\"\n");

        var (exitCode, output) = Shell.Run(app, "pnpm", "format:check");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/unformatted.ts");
    }

    [Fact]
    public void The_generated_ci_checks_formatting_after_installing_the_workspace()
    {
        using var project = new GeneratedProject(Profile);
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));

        ci.ShouldContain("      - run: pnpm format:check\n        working-directory: web/app\n");
        ci.IndexOf("run: pnpm format:check", StringComparison.Ordinal)
            .ShouldBeGreaterThan(ci.IndexOf("run: pnpm install --frozen-lockfile", StringComparison.Ordinal));
    }

    [Fact]
    public void The_app_host_runs_the_web_app_with_aspire()
    {
        using var project = new GeneratedProject(Profile);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs")).ShouldContain("AddViteApp(\"web\", \"../../web/app\")");
        project.Build();
    }

    [Fact]
    public void The_generated_ci_runs_the_end_to_end_tests_against_the_running_application()
    {
        using var project = new GeneratedProject(Profile);
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));

        ci.ShouldContain("\n  e2e:\n");
        var e2e = ci[ci.IndexOf("\n  e2e:\n", StringComparison.Ordinal)..];
        e2e.ShouldContain("playwright install --with-deps chromium");
        e2e.ShouldContain("run: bash .github/scripts/e2e.sh");
        File.Exists(Path.Combine(project.ProjectDirectory, ".github", "scripts", "e2e.sh")).ShouldBeTrue();
    }

    [Fact]
    public void The_lockfile_is_the_first_step_and_ci_installs_only_from_it()
    {
        using var project = new GeneratedProject(Profile);
        var readme = File.ReadAllText(Path.Combine(project.ProjectDirectory, "README.md"));
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));

        readme.ShouldContain("## First steps");
        readme[readme.IndexOf("## First steps", StringComparison.Ordinal)..].ShouldContain("pnpm install");
        ci.ShouldContain("web/pnpm-lock.yaml is missing");
        ci.ShouldNotContain("run: pnpm install\n");
        ci.ShouldContain("run: pnpm install --frozen-lockfile");
    }
}

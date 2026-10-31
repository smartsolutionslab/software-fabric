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
    public void The_app_host_runs_the_web_app_with_aspire()
    {
        using var project = new GeneratedProject(Profile);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs")).ShouldContain("AddViteApp(\"web\", \"../../web/app\")");
        project.Build();
    }
}

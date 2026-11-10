namespace Fabric.Packs.Tests;

/// <summary>The running application: the AppHost serves each module's remote, and the shell shows the module's page from it.</summary>
[Collection(FixedPorts.Name)]
public sealed class MicroFrontendsEndToEndTests
{
    [Theory]
    [InlineData("react", "module-federation", "tokens-and-components")]
    [InlineData("react", "native-federation", "tokens-and-components")]
    [InlineData("vue", "native-federation", "tokens")]
    [InlineData("vue", "native-federation", "web-components")]
    [InlineData("react", "web-components", "tokens-and-components")]
    [InlineData("vue", "web-components", "web-components")]
    public void A_modules_page_comes_from_its_own_remote_under_aspire(string framework, string technique, string designSystem)
    {
        using var project = new GeneratedProject(
            $"saas-standard-{framework}",
            "frontend.architecture=micro-frontends",
            $"frontend.design-system={designSystem}",
            $"frontend.mfe-technique={technique}");
        project.AddModule("billing");
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        // The sign-in test and billing's three: the module's page, loaded from the remote's own dev server, and loaded again
        // without a reload once the remote is back, after its entry or the module of its page failed to load.
        File.Exists(Path.Combine(project.ProjectDirectory, "web", "app", "e2e", "remotes", "billing.spec.ts")).ShouldBeTrue("billing's end-to-end test");

        // The generated CI's own end-to-end step: start the AppHost, wait for the shell, run Playwright, stop. On CI,
        // Playwright reports dots rather than test titles, so the count says that all four ran.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("4 passed");
    }
}

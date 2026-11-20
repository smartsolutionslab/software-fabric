using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.pwa.yes in the running application (#278): once the service worker has the app shell, a signed-in user's
/// reload offline still shows the app, and an /api answer is never served from the worker's cache.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class PwaEndToEndTests
{
    [Fact]
    public void The_shell_loads_offline_end_to_end_and_a_worker_that_caches_api_answers_fails_the_test()
    {
        using var project = new GeneratedProject("saas-standard-react", "frontend.pwa=yes");
        project.AddModule("billing");
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        var testResults = Path.Combine(web, "app", "test-results");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);

        // The generated CI's own end-to-end step: start the AppHost, wait for the app, run Playwright, stop.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, Explained(e2eExitCode, e2eOutput, testResults));
        e2eOutput.ShouldContain("7 passed"); // and the two glass checks (#698), and the billing page's three sample tests (#734)

        // The gate: a worker that keeps every same-origin answer, /api included, fails the offline test.
        var config = Path.Combine(web, "app", "vite.config.ts");
        var shellOnly = File.ReadAllText(config);
        var everything = UrlPattern().Replace(shellOnly, "urlPattern: ({ sameOrigin }) => sameOrigin,\n${indent}handler:", 1);
        everything.ShouldNotBe(shellOnly);
        File.WriteAllText(config, everything);
        // Such a worker also shows the billing page a stale list after a reload, which its sample's spec (#734) reports
        // too; this gate is the offline test's alone.
        File.Delete(Path.Combine(web, "app", "e2e", "billing-samples.spec.ts"));

        var (brokenExitCode, broken) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        // A run that failed elsewhere than the gate's verdict says what the browser saw too, since CI keeps no trace (#782).
        OfflineGate.ShouldBeItsVerdict(brokenExitCode, Explained(brokenExitCode, broken, testResults));
    }

    [Fact]
    public void Without_an_identity_provider_the_shell_loads_offline_end_to_end()
    {
        using var project = new GeneratedProject("saas-standard-react", "frontend.pwa=yes", "backend.auth=none", "backend.tenancy=none");
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);

        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, Explained(e2eExitCode, e2eOutput, Path.Combine(web, "app", "test-results")));
        e2eOutput.ShouldContain("4 passed");
    }

    /// <summary>A failed run's output, and what the browser saw, since CI keeps no Playwright artifact (#791).</summary>
    private static string Explained(int exitCode, string output, string testResults) =>
        exitCode == 0 ? output : $"{output}\n{PlaywrightFailure.Describe(testResults)}";

    [GeneratedRegex(@"urlPattern: [\s\S]*?,\n(?<indent>\s*)handler:")]
    private static partial Regex UrlPattern();
}

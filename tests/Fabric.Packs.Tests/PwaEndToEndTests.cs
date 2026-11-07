using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.pwa.yes in the running application (#278): once the service worker has the app shell, a signed-in user's
/// reload offline still shows the app, and an /api answer is never served from the worker's cache.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class PwaEndToEndTests
{
    private const string OfflineTest = "the app shell loads offline, and /api is never answered from the service worker";

    [Fact]
    public void The_shell_loads_offline_end_to_end_and_a_worker_that_caches_api_answers_fails_the_test()
    {
        using var project = new GeneratedProject("saas-standard-react", "frontend.pwa=yes");
        project.AddModule("billing");
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);

        // The generated CI's own end-to-end step: start the AppHost, wait for the app, run Playwright, stop.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain(OfflineTest);
        e2eOutput.ShouldNotContain("failed");

        // The gate: a worker that keeps every same-origin answer, /api included, fails the offline test.
        var config = Path.Combine(web, "app", "vite.config.ts");
        var shellOnly = File.ReadAllText(config);
        var everything = UrlPattern().Replace(shellOnly, "urlPattern: ({ sameOrigin }) => sameOrigin,\n${indent}handler:", 1);
        everything.ShouldNotBe(shellOnly);
        File.WriteAllText(config, everything);

        var (brokenExitCode, broken) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        brokenExitCode.ShouldNotBe(0, broken);
        broken.ShouldContain($"1 failed\n    [chromium] › e2e/offline.spec.ts");
        broken.ShouldContain("Received: 200");
    }

    [GeneratedRegex(@"urlPattern: [\s\S]*?,\n(?<indent>\s*)handler:")]
    private static partial Regex UrlPattern();
}

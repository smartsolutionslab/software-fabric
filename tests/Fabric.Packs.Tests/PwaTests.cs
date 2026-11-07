namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.pwa.yes (#278): vite-plugin-pwa builds a service worker and a web app manifest, registered by a script it writes
/// into index.html; the worker keeps the app shell and never /api or the identity provider.
/// </summary>
public sealed class PwaTests
{
    private const string Pwa = "frontend.pwa=yes";

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    public void The_build_emits_a_service_worker_and_a_manifest_that_index_html_registers(string profile)
    {
        using var project = new GeneratedProject(profile, Pwa);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{format}");

        var dist = Path.Combine(web, "app", "dist");
        File.Exists(Path.Combine(dist, "sw.js")).ShouldBeTrue("dist/sw.js");
        File.Exists(Path.Combine(dist, "manifest.webmanifest")).ShouldBeTrue("dist/manifest.webmanifest");
        var index = File.ReadAllText(Path.Combine(dist, "index.html"));
        index.ShouldContain("<link rel=\"manifest\" href=\"/manifest.webmanifest\">");
        index.ShouldContain("src=\"/registerSW.js\"");

        // Navigations to /api are the host's, never the shell's.
        File.ReadAllText(Path.Combine(dist, "sw.js")).ShouldContain("denylist:[/^\\/api\\//]");
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    public void Without_the_option_no_service_worker_is_generated(string profile)
    {
        using var project = new GeneratedProject(profile);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "vite.config.ts")).ShouldNotContain("VitePWA");
        File.Exists(Path.Combine(project.ProjectDirectory, "web", "app", "e2e", "offline.spec.ts")).ShouldBeFalse();
    }
}

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

        // Navigations to anything the dev proxy forwards (/api...) are the host's, never the shell's.
        File.ReadAllText(Path.Combine(dist, "sw.js")).ShouldContain("denylist:[/^\\/api/]");
    }

    /// <summary>
    /// The production build under vite preview, in Chromium: once the worker has the page, the shell reloads offline from
    /// its precache, and a navigation to anything the dev proxy forwards to the host (/api, /api/...) is never answered
    /// with the shell.
    /// </summary>
    private const string OfflineBuildCheck = """
        import { chromium } from '@playwright/test';
        import { preview } from 'vite';

        const server = await preview({ preview: { port: 0 }, logLevel: 'silent' });
        const url = server.resolvedUrls.local[0];
        const browser = await chromium.launch();
        try {
          const context = await browser.newContext();
          const page = await context.newPage();
          await page.goto(url);
          await page.waitForFunction(() => navigator.serviceWorker.controller !== null, null, { timeout: 15_000 });

          await context.setOffline(true);
          await page.reload();
          await page.getByRole('heading', { name: 'shop' }).waitFor({ timeout: 5_000 });
          console.log('shell offline: loaded');

          for (const path of ['/api', '/api?page=1', '/api/me']) {
            const outcome = await page.goto(new URL(path, url).href).then(
              () => 'answered with the shell',
              () => 'unreachable',
            );
            console.log(`${path} offline: ${outcome}`);
          }
        } finally {
          await browser.close();
          await server.close();
        }
        """;

    [Fact]
    public void The_built_shell_reloads_offline_and_a_navigation_to_api_is_never_answered_with_it()
    {
        using var project = new GeneratedProject("saas-standard-react", Pwa, "backend.auth=none", "backend.tenancy=none");
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        project.Write("web/app/offline-build.mjs", OfflineBuildCheck);
        var (checkExitCode, check) = Shell.Run(Path.Combine(web, "app"), "node", "offline-build.mjs");

        checkExitCode.ShouldBe(0, check);
        check.ShouldContain("shell offline: loaded");
        check.ShouldContain("/api offline: unreachable");
        check.ShouldContain("/api?page=1 offline: unreachable");
        check.ShouldContain("/api/me offline: unreachable");
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

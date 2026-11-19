using System.Net;
using System.Net.Sockets;

namespace Fabric.Packs.Tests;

/// <summary>
/// The generated web app's dev server pre-bundles every dependency the app imports before its first page load (#791). A
/// dependency Vite finds only while a page loads is bundled again, and the page's requests for the bundles it was already
/// handed answer 504 "Outdated Optimize Dep": the app never renders. Vite keeps its pre-bundle in node_modules/.vite and
/// trusts it while the lockfile and the config are unchanged, whatever the app's imports have become since: a module added
/// after the last run, or in CI a page written after the integration tests' AppHost ran the dev server (VueSignInTests),
/// whose scan had failed on the deleted sample's imports and left the pre-bundle empty.
/// </summary>
public sealed class DevServerPrebundlingTests
{
    /// <summary>Starts the dev server as `pnpm dev` does (its code generators first) and loads the app's start page once.</summary>
    private const string PlaywrightConfig = """
        import { defineConfig, devices } from '@playwright/test';

        const port = Number(process.env.PREBUNDLE_PORT);

        export default defineConfig({
          testDir: './prebundle',
          use: { baseURL: `http://localhost:${port}` },
          projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
          webServer: {
            command: `pnpm dev --port ${port} --strictPort`,
            url: `http://localhost:${port}`,
            reuseExistingServer: false,
            stdout: 'pipe',
          },
        });
        """;

    private const string StartPageSpec = """
        import { test } from '@playwright/test';

        test('the start page loads', async ({ page }) => {
          await page.goto('/', { waitUntil: 'commit' });
          await page.waitForLoadState('networkidle');
          // Vite logs the dependencies it optimized late 200 ms after it has bundled them.
          await page.waitForTimeout(1000);
        });
        """;

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    public void The_dev_server_prebundles_every_dependency_the_app_imports_before_its_first_page_load(string profile)
    {
        using var project = new GeneratedProject(profile);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");
        project.Write("web/app/prebundle.config.ts", PlaywrightConfig);
        project.Write("web/app/prebundle/start-page.spec.ts", StartPageSpec);

        // The first run pre-bundles what the app imports without a module; the module's page imports more (its table and form).
        var (firstExitCode, firstOutput) = LoadStartPage(web);
        firstExitCode.ShouldBe(0, firstOutput);
        project.AddModule("billing");

        var (exitCode, output) = LoadStartPage(web);

        exitCode.ShouldBe(0, output);
        output.ShouldNotContain("optimized dependencies changed", customMessage: output);
        output.ShouldNotMatch(@"dependenc(y|ies) optimized:", customMessage: output);
    }

    private static (int ExitCode, string Output) LoadStartPage(string web)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return Shell.Run(
            Path.Combine(web, "app"),
            new Dictionary<string, string> { ["PREBUNDLE_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            "pnpm",
            "exec",
            "playwright",
            "test",
            "--config",
            "prebundle.config.ts");
    }
}

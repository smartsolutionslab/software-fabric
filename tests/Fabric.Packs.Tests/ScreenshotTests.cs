using System.Text.Json;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.test.extras: screenshot-tests</c> (M3-60): Playwright's <c>toHaveScreenshot</c> on the start page of the
/// running application, compared with baselines the project commits under <c>web/app/e2e/__screenshots__</c>. Only
/// Playwright's container, pinned by digest, renders them, so a baseline does not differ by machine; the generator
/// writes none. e2e.sh runs them after the end-to-end tests, through the <c>e2e.after-tests</c> slot.
/// </summary>
public sealed partial class ScreenshotTests
{
    private const string Screenshots = "frontend.test.extras=accessibility,screenshot-tests";
    private const string Spec = "web/app/e2e/start-page.screenshot.ts";
    private const string Config = "web/app/e2e/screenshots.config.ts";
    private const string Script = ".github/scripts/screenshots.sh";

    public static TheoryData<string> Profiles { get; } = ["saas-standard-react", "saas-standard-vue"];

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_project_with_screenshot_tests_generates_from_the_real_packs_alone(string profile)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, [Screenshots]);

        generated.Errors.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_start_page_is_compared_with_its_committed_screenshot_after_signing_in(string profile)
    {
        var files = Files(profile, Screenshots);

        var spec = files[Spec];
        spec.ShouldContain("await page.goto('/');");
        spec.ShouldContain(".getByRole('button', { name: 'Sign In' }).click();");
        spec.ShouldContain("await expect(page.getByRole('heading', { name: 'shop' })).toBeVisible();");
        spec.ShouldContain("await expect(page).toHaveScreenshot('start-page.png', { fullPage: true });");

        var config = files[Config];
        config.ShouldContain("testMatch: '*.screenshot.ts'");
        config.ShouldContain("snapshotPathTemplate: '{testDir}/__screenshots__/{testFilePath}/{arg}{ext}'");
    }

    [Fact]
    public void Without_authentication_the_start_page_is_compared_without_signing_in()
    {
        var spec = Files("lean-crud", Screenshots, "backend.auth=none")[Spec];

        spec.ShouldNotContain("Sign In");
        spec.ShouldContain("await expect(page).toHaveScreenshot('start-page.png', { fullPage: true });");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_end_to_end_tests_never_run_the_screenshot_tests_with_the_hosts_browser(string profile)
    {
        var files = Files(profile, Screenshots);

        // The end-to-end config's default testMatch takes *.spec.ts and *.test.ts; the screenshot tests are neither.
        files["web/app/playwright.config.ts"].ShouldContain("testDir: './e2e'");
        files["web/app/playwright.config.ts"].ShouldNotContain("testMatch");
        files.Keys.Where(path => path.StartsWith("web/app/e2e/", StringComparison.Ordinal) && path.Contains("screenshot", StringComparison.Ordinal))
            .ShouldAllBe(path => !path.EndsWith(".spec.ts", StringComparison.Ordinal) && !path.EndsWith(".test.ts", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_screenshots_run_in_playwrights_container_pinned_by_digest_at_the_version_of_playwright_test(string profile)
    {
        var files = Files(profile, Screenshots);

        var script = files[Script];
        var image = ImagePattern().Match(script);
        image.Success.ShouldBeTrue(script);
        using var package = JsonDocument.Parse(files["web/app/package.json"]);
        var playwright = package.RootElement.GetProperty("devDependencies").GetProperty("@playwright/test").GetString();
        image.Groups["version"].Value.ShouldBe(playwright);

        script.ShouldContain("--platform linux/amd64");
        script.ShouldContain("--network host");
        script.ShouldContain("--config e2e/screenshots.config.ts");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_web_app_compares_and_updates_screenshots_and_e2e_sh_compares_them_while_the_application_runs(string profile)
    {
        var files = Files(profile, Screenshots);

        using var package = JsonDocument.Parse(files["web/app/package.json"]);
        var scripts = package.RootElement.GetProperty("scripts");
        scripts.GetProperty("screenshots").GetString().ShouldBe("bash ../../.github/scripts/screenshots.sh");
        scripts.GetProperty("screenshots:update").GetString().ShouldBe("bash ../../.github/scripts/screenshots.sh --update");

        files[".github/scripts/e2e.sh"].ShouldEndWith(
            "(cd web/app && pnpm e2e) || fail \"The end-to-end tests failed.\"\n" +
            "(cd web/app && pnpm screenshots) || fail \"The screenshot tests failed.\"\n");
    }

    [Fact]
    public void The_script_refuses_to_run_when_playwright_test_is_another_version_than_the_image()
    {
        var root = Directory.CreateTempSubdirectory("fabric-screenshots-").FullName;
        try
        {
            var script = Path.Combine(root, ".github", "scripts", "screenshots.sh");
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            File.WriteAllText(script, Files("saas-standard-react", Screenshots)[Script]);
            var package = Path.Combine(root, "web", "app", "node_modules", "@playwright", "test");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "package.json"), """{ "name": "@playwright/test", "version": "9.9.9" }""");

            var (exitCode, output) = Shell.Run(root, "bash", script);

            exitCode.ShouldBe(1, output);
            output.ShouldContain("@playwright/test is 9.9.9, but .github/scripts/screenshots.sh runs Playwright 1.63.0's image: change both together.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_generator_writes_no_baseline(string profile)
    {
        var files = Files(profile, Screenshots);

        files.Keys.ShouldNotContain(path => path.Contains("__screenshots__", StringComparison.Ordinal) || path.EndsWith(".png", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Without_the_option_there_are_no_screenshot_tests(string profile)
    {
        var files = Files(profile);

        files.Keys.ShouldNotContain(Spec);
        files.Keys.ShouldNotContain(Config);
        files.Keys.ShouldNotContain(Script);
        files["web/app/package.json"].ShouldNotContain("screenshots");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_agent_rules_say_how_to_update_a_baseline(string profile)
    {
        var agents = Files(profile, Screenshots)["AGENTS.md"];

        agents.ShouldContain("## Screenshot tests");
        agents.ShouldContain("pnpm screenshots:update");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"mcr\.microsoft\.com/playwright:v(?<version>\d+\.\d+\.\d+)-noble@sha256:[0-9a-f]{64}")]
    private static partial Regex ImagePattern();
}

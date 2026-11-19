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
    private const string ModuleSpec = "web/app/e2e/billing-page.screenshot.ts";

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
            output.ShouldContain("@playwright/test is 9.9.9, but .github/scripts/screenshots.sh runs Playwright 1.65.1's image: change both together.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("saas-standard-react", "useGetBillingSamplesQuery")]
    [InlineData("saas-standard-vue", "useGetBillingSamples")]
    public void Each_module_page_is_compared_in_the_light_and_the_dark_theme_showing_its_data_files_example(string profile, string hook)
    {
        var files = AddModule(profile, Screenshots);

        var spec = files[ModuleSpec];
        spec.ShouldContain("import { examples } from '../src/modules/billing/BillingPage.data';");
        spec.ShouldContain($"const samples = examples.{hook} as {{ items: {{ name: string }}[] }};");
        spec.ShouldContain("for (const colorScheme of ['light', 'dark'] as const) {");
        spec.ShouldContain("await page.emulateMedia({ colorScheme });");
        spec.ShouldContain(".getByRole('button', { name: 'Sign In' }).click();");
        spec.ShouldContain("await page.route(/\\/api\\/billing\\/samples\\?/, (route) => route.fulfill({ json: samples }));");
        spec.ShouldContain("await page.goto('/billing');");
        spec.ShouldContain("await expect(page.getByText(name, { exact: true })).toBeVisible();");
        spec.ShouldContain("await page.evaluate(async () => {\n    await document.fonts.ready;\n  });");
        spec.ShouldContain("await expect(page).toHaveScreenshot(`billing-page-${colorScheme}.png`, { fullPage: true });");
        files.Keys.ShouldNotContain(path => path.Contains("__screenshots__", StringComparison.Ordinal) || path.EndsWith(".png", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_accessibility_tests_data_file_the_module_page_shows_the_same_example_written_in_its_screenshot_test()
    {
        var files = AddModule("saas-standard-react", "frontend.test.extras=screenshot-tests");

        files.Keys.ShouldNotContain("web/app/src/modules/billing/BillingPage.data.ts");
        var spec = files[ModuleSpec];
        spec.ShouldNotContain("BillingPage.data");
        spec.ShouldContain("const samples = {\n  items: [\n    { id: '0198c1a0-0000-7000-8000-000000000001', name: 'Ada' },\n    { id: '0198c1a0-0000-7000-8000-000000000002', name: 'Bea' },\n  ],\n  totalCount: 2,\n  page: 1,\n  pageSize: 20,\n};");
        spec.ShouldContain("route.fulfill({ json: samples })");
    }

    [Fact]
    public void Without_authentication_the_module_page_is_compared_without_signing_in()
    {
        var spec = AddModule("lean-crud", Screenshots, "backend.auth=none")[ModuleSpec];

        spec.ShouldNotContain("Sign In");
        spec.ShouldContain("await expect(page).toHaveScreenshot(`billing-page-${colorScheme}.png`, { fullPage: true });");
    }

    [Theory]
    [InlineData("saas-standard-react", "frontend.ui-library=radix-headless")]
    [InlineData("saas-standard-vue", "frontend.ui-library=vuetify")]
    public void A_module_page_without_the_sample_gets_no_screenshot_test(string profile, string uiLibrary)
    {
        var files = AddModule(profile, Screenshots, uiLibrary);

        files.Keys.ShouldNotContain(ModuleSpec);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_screenshot_is_compared_within_a_stated_threshold_with_animations_stopped(string profile)
    {
        var config = Files(profile, Screenshots)[Config];

        config.ShouldContain("toHaveScreenshot: { maxDiffPixels: 100, threshold: 0.2, animations: 'disabled', caret: 'hide' }");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_failed_comparison_uploads_its_expected_actual_and_diff_images_from_the_e2e_job(string profile)
    {
        var workflow = Files(profile, Screenshots)[".github/workflows/ci.yml"];

        workflow.ShouldContain(
            "      - run: bash .github/scripts/e2e.sh\n" +
            "\n" +
            "      # A screenshot that differs from its baseline: the baseline, what rendered and the difference between them.\n" +
            "      - if: failure()\n" +
            "        uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2\n" +
            "        with:\n" +
            "          name: screenshot-diffs\n" +
            "          path: web/app/test-results/screenshots\n" +
            "          if-no-files-found: ignore\n");
        Files(profile)[".github/workflows/ci.yml"].ShouldNotContain("screenshot-diffs");
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
        agents.ShouldContain("`web/app/e2e/<module>-page.screenshot.ts` compares each module's page in the light and the dark theme");
        agents.ShouldContain("Update a baseline only when a change you meant alters how a page looks");
        agents.ShouldContain("the `screenshot-diffs` artifact");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Without_the_option_a_module_gets_no_screenshot_test(string profile)
    {
        AddModule(profile).Keys.ShouldNotContain(ModuleSpec);
    }

    /// <summary>The files <c>fabric add-module billing</c> creates in a project of the profile with the overrides.</summary>
    private static Dictionary<string, string> AddModule(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var fabricYaml = generated.Files.Single(file => file.Path == FabricFile.FileName).Content;
        var record = FabricFile.Read(new StringReader(fabricYaml), FabricFile.FileName);

        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, "billing", []);

        addition.Errors.ShouldBeEmpty();
        return addition.Created.ToDictionary(file => file.Path, file => file.Content);
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

using System.Text.Json;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.test.extras.accessibility (#604): every module screen of a single-page app, <c>web/app/src/modules/</c>,
/// gets an axe check beside it, from <c>fabric new</c> and <c>fabric add-module</c> alike, and the generated
/// <c>src/screens.test.ts</c> fails while a module screen has none. The generated check is <c>&lt;Name&gt;Page.axe.test.*</c>,
/// so the screen's own component test, <c>&lt;Name&gt;Page.test.*</c>, stays the project's; once that test checks the
/// screen with axe, the generated one skips itself (#627).
/// </summary>
public sealed class ModuleScreenAccessibilityTests
{
    private static readonly string[] AgentsAndDashboards =
    [
        "backend.llm=openai",
        "backend.llm.agents=agent-framework",
        "frontend.agent-ui=ag-ui",
        "frontend.dashboards=user-editable,generative",
    ];

    private const string ReactOwnTest = """
        import { render } from '@testing-library/react';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import { BillingPage } from './BillingPage';

        test('our billing page passes axe', async () => {
          const { container } = render(<BillingPage />);

          expect(await axe(container)).toHaveNoViolations();
        });

        """;

    private const string VueOwnTest = """
        import { render } from '@testing-library/vue';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import BillingPage from './BillingPage.vue';

        test('our billing page passes axe', async () => {
          const { container } = render(BillingPage);

          expect(await axe(container)).toHaveNoViolations();
        });

        """;

    public static TheoryData<string, string> Routers => new()
    {
        { "saas-standard-react", "frontend.routing=react-router" },
        { "saas-standard-react", "frontend.routing=tanstack-router" },
        { "saas-standard-vue", "frontend.routing=vue-router" },
    };

    [Theory]
    [MemberData(nameof(Routers))]
    public void The_screen_add_module_writes_has_an_axe_check_beside_it(string profile, string routing)
    {
        using var project = new GeneratedProject(profile, routing);
        project.AddModule("billing");

        Screens(project).ShouldNotBeEmpty();
        UncheckedScreens(project).ShouldBeEmpty();
        Screens(project).ShouldAllBe(screen => !File.Exists(Path.Combine(project.ProjectDirectory, OwnTestOf(screen))));
    }

    [Fact]
    public void The_assistant_and_dashboards_screens_have_an_axe_check_beside_them_with_react()
    {
        using var project = new GeneratedProject("saas-ai-react");

        Screens(project).Count.ShouldBe(2);
        UncheckedScreens(project).ShouldBeEmpty();
    }

    [Fact]
    public void The_assistant_and_dashboards_screens_have_an_axe_check_beside_them_with_vue()
    {
        using var project = new GeneratedProject("saas-standard-vue", AgentsAndDashboards);

        Screens(project).Count.ShouldBe(2);
        UncheckedScreens(project).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routers))]
    public void The_generated_axe_check_runs_until_the_screens_own_test_checks_it_and_the_web_tests_fail_once_neither_does(
        string profile,
        string routing)
    {
        using var project = new GeneratedProject(profile, routing);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var generated = TestOf(screen);

        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");
        Statuses(project).GetValueOrDefault(generated).ShouldBe(["passed"]);

        project.Write(OwnTestOf(screen), screen.EndsWith(".vue", StringComparison.Ordinal) ? VueOwnTest : ReactOwnTest);
        var statuses = Statuses(project);
        statuses.GetValueOrDefault(OwnTestOf(screen)).ShouldBe(["passed"]);
        statuses.GetValueOrDefault(generated).ShouldBe(["skipped"]);

        File.Delete(Path.Combine(project.ProjectDirectory, OwnTestOf(screen)));
        File.Delete(Path.Combine(project.ProjectDirectory, generated));
        var (uncheckedExitCode, uncheckedOutput) = Shell.Run(web, "pnpm", "test");
        uncheckedExitCode.ShouldNotBe(0, $"pnpm test passed without {generated}:\n{uncheckedOutput}");
        uncheckedOutput.ShouldContain($"./modules/billing/{Path.GetFileName(screen)}");
    }

    /// <summary>
    /// Runs the web app's tests, which must pass, and answers the statuses vitest reports for each test file's tests
    /// (<c>passed</c>, <c>skipped</c>, ...), by the file's path relative to the project.
    /// </summary>
    private static Dictionary<string, List<string>> Statuses(GeneratedProject project)
    {
        var app = Path.Combine(project.ProjectDirectory, "web", "app");
        var report = Path.Combine(project.ProjectDirectory, "vitest-report.json");
        var (exitCode, output) = Shell.Run(app, "pnpm", "test", "--reporter=json", $"--outputFile={report}");
        exitCode.ShouldBe(0, $"pnpm test:\n{output}");

        using var json = JsonDocument.Parse(File.ReadAllText(report));
        return json.RootElement.GetProperty("testResults").EnumerateArray().ToDictionary(
            result => Path.GetRelativePath(project.ProjectDirectory, result.GetProperty("name").GetString()!).Replace('\\', '/'),
            result => result.GetProperty("assertionResults").EnumerateArray().Select(test => test.GetProperty("status").GetString()!).ToList());
    }

    /// <summary>Every module screen of the web app, relative to the project: a <c>*Page.tsx</c> or <c>*Page.vue</c>.</summary>
    private static List<string> Screens(GeneratedProject project)
    {
        var modules = Path.Combine(project.ProjectDirectory, "web", "app", "src", "modules");
        return [.. Directory.EnumerateFiles(modules, "*Page.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith("Page.tsx", StringComparison.Ordinal) || file.EndsWith("Page.vue", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(project.ProjectDirectory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The screens without the generated axe check beside them.</summary>
    private static List<string> UncheckedScreens(GeneratedProject project) =>
        [.. Screens(project).Where(screen =>
        {
            var test = Path.Combine(project.ProjectDirectory, TestOf(screen));
            return !File.Exists(test) || !File.ReadAllText(test).Contains("expect(await axe(container)).toHaveNoViolations()", StringComparison.Ordinal);
        })];

    /// <summary>The axe check the generator writes beside a screen: <c>BillingPage.axe.test.tsx</c>.</summary>
    private static string TestOf(string screen) =>
        screen.EndsWith(".vue", StringComparison.Ordinal) ? screen[..^".vue".Length] + ".axe.test.ts" : screen[..^".tsx".Length] + ".axe.test.tsx";

    /// <summary>The screen's own component test, which the generator leaves to the project: <c>BillingPage.test.tsx</c>.</summary>
    private static string OwnTestOf(string screen) =>
        screen.EndsWith(".vue", StringComparison.Ordinal) ? screen[..^".vue".Length] + ".test.ts" : screen[..^".tsx".Length] + ".test.tsx";
}

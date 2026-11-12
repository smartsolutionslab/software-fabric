using System.Text.Json;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.test.extras.accessibility (#604): every module screen of a single-page app, <c>web/app/src/modules/</c>,
/// gets an axe check beside it, from <c>fabric new</c> and <c>fabric add-module</c> alike, and the generated
/// <c>src/screens.test.ts</c> fails while a module screen has none. The check is <c>&lt;Name&gt;Page.axe.test.*</c>, so the
/// screen's own component test, <c>&lt;Name&gt;Page.test.*</c>, stays the project's (#627). It always runs, whatever the
/// project's own test says, and stands in for the generated API client itself, so it checks a real screen.
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

    /// <summary>A real screen: it asks the generated client who is signed in, and its field has no label.</summary>
    private const string ReactScreen = """
        import { useGetMeQuery } from '@/api/generated';

        export function BillingPage() {
          const { data } = useGetMeQuery();
          return (
            <main>
              <h1>Billing</h1>
              <p>{data?.name ?? 'Who is signed in is still loading.'}</p>
              <input type="text" />
            </main>
          );
        }

        """;

    private const string VueScreen = """
        <script setup lang="ts">
        import { useGetMe } from '@/api/generated';

        const { data } = useGetMe();
        </script>

        <template>
          <main>
            <h1>Billing</h1>
            <p>{{ data?.name ?? 'Who is signed in is still loading.' }}</p>
            <input type="text" />
          </main>
        </template>

        """;

    private const string UnlabelledField = """<input type="text" />""";

    /// <summary>The screen's own test, its axe check skipped.</summary>
    private const string ReactSkippedOwnTest = """
        import { render } from '@testing-library/react';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import { BillingPage } from './BillingPage';

        test.skip('our billing page passes axe', async () => {
          const { container } = render(<BillingPage />);

          expect(await axe(container)).toHaveNoViolations();
        });

        """;

    private const string VueSkippedOwnTest = """
        import { render } from '@testing-library/vue';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import BillingPage from './BillingPage.vue';

        test.skip('our billing page passes axe', async () => {
          const { container } = render(BillingPage);

          expect(await axe(container)).toHaveNoViolations();
        });

        """;

    /// <summary>The screen's own test, its axe check commented out.</summary>
    private const string CommentedOutOwnTest = """
        import { expect, test } from 'vitest';

        test('our billing page is still to be tested', () => {
          // expect(await axe(container)).toHaveNoViolations();
          expect('Billing').toBe('Billing');
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

    /// <summary>Each router, with the screen's own test skipping its axe check or with it commented out.</summary>
    public static TheoryData<string, string, string> RoutersAndOwnTests => new()
    {
        { "saas-standard-react", "frontend.routing=react-router", "skipped" },
        { "saas-standard-react", "frontend.routing=react-router", "commented-out" },
        { "saas-standard-react", "frontend.routing=tanstack-router", "skipped" },
        { "saas-standard-react", "frontend.routing=tanstack-router", "commented-out" },
        { "saas-standard-vue", "frontend.routing=vue-router", "skipped" },
        { "saas-standard-vue", "frontend.routing=vue-router", "commented-out" },
    };

    [Theory]
    [MemberData(nameof(RoutersAndOwnTests))]
    public void The_generated_axe_check_runs_on_a_real_screen_whatever_its_own_test_does_and_the_web_tests_fail_without_it(
        string profile,
        string routing,
        string ownTest)
    {
        using var project = new GeneratedProject(profile, routing);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var vue = screen.EndsWith(".vue", StringComparison.Ordinal);
        var generated = TestOf(screen);
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        project.Write(screen, vue ? VueScreen : ReactScreen);
        project.Write(OwnTestOf(screen), ownTest == "skipped" ? (vue ? VueSkippedOwnTest : ReactSkippedOwnTest) : CommentedOutOwnTest);
        var (violationExitCode, violationOutput, violation) = Statuses(project);
        violationExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label:\n{violationOutput}");
        violation.GetValueOrDefault(generated).ShouldBe(["failed"], violationOutput);

        project.Write(screen, (vue ? VueScreen : ReactScreen).Replace(UnlabelledField, """<input type="text" aria-label="Invoice number" />""", StringComparison.Ordinal));
        var (labelledExitCode, labelledOutput, labelled) = Statuses(project);
        labelledExitCode.ShouldBe(0, $"pnpm test:\n{labelledOutput}");
        labelled.GetValueOrDefault(generated).ShouldBe(["passed"]);

        File.Delete(Path.Combine(project.ProjectDirectory, generated));
        var (uncheckedExitCode, uncheckedOutput) = Shell.Run(web, "pnpm", "test");
        uncheckedExitCode.ShouldNotBe(0, $"pnpm test passed without {generated}:\n{uncheckedOutput}");
        uncheckedOutput.ShouldContain($"./modules/billing/{Path.GetFileName(screen)}");
    }

    /// <summary>
    /// Runs the web app's tests and answers their exit code and output, and the statuses vitest reports for each test
    /// file's tests (<c>passed</c>, <c>failed</c>, <c>skipped</c>, ...), by the file's path relative to the project.
    /// </summary>
    private static (int ExitCode, string Output, Dictionary<string, List<string>> Statuses) Statuses(GeneratedProject project)
    {
        var app = Path.Combine(project.ProjectDirectory, "web", "app");
        var report = Path.Combine(project.ProjectDirectory, "vitest-report.json");
        File.Delete(report);
        var (exitCode, output) = Shell.Run(app, "pnpm", "test", "--reporter=json", $"--outputFile={report}");
        File.Exists(report).ShouldBeTrue($"vitest wrote no report:\n{output}");

        using var json = JsonDocument.Parse(File.ReadAllText(report));
        var statuses = json.RootElement.GetProperty("testResults").EnumerateArray().ToDictionary(
            result => Path.GetRelativePath(project.ProjectDirectory, result.GetProperty("name").GetString()!).Replace('\\', '/'),
            result => result.GetProperty("assertionResults").EnumerateArray().Select(test => test.GetProperty("status").GetString()!).ToList());
        return (exitCode, output, statuses);
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

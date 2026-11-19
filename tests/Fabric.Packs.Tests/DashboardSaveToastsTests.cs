using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #735: where the app shell has toasts (shadcn with react-router or TanStack Router, #695 and #736, or shadcn-vue with
/// vue-router, in a single-page app), the dashboards editor reports Save's success and failure through <c>notify</c>
/// from <c>@/lib/notify</c>: a toast that is a status message or an alert, not text of its own beside the Save button.
/// Without the shell (the other UI libraries) the editor keeps its inline notice.
/// </summary>
public sealed partial class DashboardSaveToastsTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string Dashboards = "frontend.dashboards=user-editable";
    private const string ReactCanvas = "web/app/src/dashboards/DashboardCanvas.tsx";
    private const string VueCanvas = "web/app/src/dashboards/DashboardCanvas.vue";
    private const string Saved = "The dashboard was saved.";
    private const string NotSaved = "The dashboard could not be saved.";

    /// <summary>Projects with the shell and the editor, by framework and by each way the editor's Save is written.</summary>
    public static TheoryData<string, string[], string> WithTheShell => new()
    {
        { React, [Dashboards], ReactCanvas },
        { React, [Dashboards, "frontend.forms=tanstack-form", "frontend.validation=valibot"], ReactCanvas },
        { "saas-ai-react", [], ReactCanvas },
        { React, [Dashboards, "frontend.routing=tanstack-router"], ReactCanvas },
        { "saas-ai-react", ["frontend.routing=tanstack-router"], ReactCanvas },
        { Vue, [Dashboards], VueCanvas },
        { Vue, [Dashboards, "frontend.forms=tanstack-form"], VueCanvas },
        { Vue, [Dashboards, "frontend.forms=formkit"], VueCanvas },
    };

    /// <summary>Projects with the editor and without the shell.</summary>
    public static TheoryData<string, string[], string> WithoutTheShell => new()
    {
        { React, [Dashboards, "frontend.ui-library=radix-headless"], ReactCanvas },
        { React, [Dashboards, "frontend.ui-library=mui", "frontend.styling=css-modules"], ReactCanvas },
        { React, [Dashboards, "frontend.routing=tanstack-router", "frontend.ui-library=radix-headless"], ReactCanvas },
        { Vue, [Dashboards, "frontend.ui-library=reka-ui"], VueCanvas },
        { Vue, [Dashboards, "frontend.ui-library=vuetify"], VueCanvas },
    };

    [Theory]
    [MemberData(nameof(WithTheShell))]
    public void With_the_shell_the_editor_reports_save_through_notify_and_shows_no_notice_of_its_own(string profile, string[] overrides, string canvas)
    {
        var files = Files(profile, overrides);

        var editor = files[canvas];
        editor.ShouldContain("import { notify } from '@/lib/notify';");
        editor.ShouldContain($"notify.success('{Saved}');");
        editor.ShouldContain($"notify.failure('{NotSaved}');");
        editor.ShouldNotContain("role=\"status\"");
        editor.ShouldNotContain("Saved.<");
        editor.ShouldNotContain($"{NotSaved}<");
        var test = files[canvas == VueCanvas ? "web/app/src/dashboards/DashboardEditor.test.ts" : "web/app/src/dashboards/DashboardEditor.test.tsx"];
        test.ShouldContain("import { Toaster } from '@/components/ui/sonner';");
        test.ShouldContain("test('saving reports in a toast that the dashboard was saved'");
        test.ShouldContain("test('a failed save reports in a toast that the dashboard could not be saved'");
        test.ShouldNotContain("takes the saved notice away");
        files["web/app/e2e/dashboards.spec.ts"].ShouldContain($"toHaveText('{Saved}')");
    }

    [Fact]
    public void With_the_shell_the_generative_end_to_end_test_finds_the_save_in_a_toast()
    {
        Files("saas-ai-react")["web/app/e2e/generative-dashboards.spec.ts"].ShouldContain($"toHaveText('{Saved}')");
    }

    [Theory]
    [MemberData(nameof(WithoutTheShell))]
    public void Without_the_shell_the_editor_keeps_its_inline_notice(string profile, string[] overrides, string canvas)
    {
        var files = Files(profile, overrides);

        var editor = files[canvas];
        editor.ShouldNotContain("notify");
        editor.ShouldContain("role=\"status\"");
        editor.ShouldContain(NotSaved);
        files["web/app/e2e/dashboards.spec.ts"].ShouldContain("toHaveText('Saved.')");
    }

    /// <summary>
    /// The web app builds, lints and formats, and the editor's own tests find Save's toasts in the shell's toaster: a
    /// status message for a save, an alert for a failed one, and nothing for a save the user edited past.
    /// </summary>
    [Theory]
    [InlineData(React, "tsx")]
    [InlineData(React, "tsx", "frontend.forms=tanstack-form", "frontend.validation=valibot")]
    [InlineData(React, "tsx", "frontend.routing=tanstack-router")]
    [InlineData(Vue, "ts")]
    public void The_editor_reports_save_in_the_shells_toasts_and_its_tests_pass(string profile, string extension, params string[] overrides)
    {
        using var project = new GeneratedProject(profile, [Dashboards, .. overrides]);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose", "src/dashboards/DashboardEditor.test");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        string[] passed =
        [
            "a user opens a dashboard, adds a widget, removes one, renames it and saves",
            "saving reports in a toast that the dashboard was saved",
            "a failed save reports in a toast that the dashboard could not be saved",
            "edits made while a save is under way are not reported as saved",
        ];
        foreach (var name in passed)
        {
            plain.ShouldContain($"✓ src/dashboards/DashboardEditor.test.{extension} > {name}", customMessage: testOutput);
        }
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

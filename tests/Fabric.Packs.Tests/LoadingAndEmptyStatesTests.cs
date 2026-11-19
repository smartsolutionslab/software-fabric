using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #696: with shadcn or shadcn-vue, every project has the registry's <c>Skeleton</c> and an <c>EmptyState</c> (an icon,
/// one sentence, the primary action), whatever its router. TanStack Table's <c>DataTable</c> draws skeleton rows in its
/// own shape while its query is pending, with <c>aria-busy</c> on the table and "Loading…" kept for screen readers, and
/// what it is given as <c>empty</c> in place of rows when it has none; its axe check covers the three states. The other
/// UI libraries and AG Grid are out of scope: their table is unchanged.
/// </summary>
public sealed partial class LoadingAndEmptyStatesTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";

    /// <summary>Every shadcn and shadcn-vue project, the ones without the app shell included.</summary>
    public static TheoryData<string, string[], bool> Shadcn => new()
    {
        { React, [], false },
        { React, ["frontend.routing=tanstack-router"], false },
        { "industrial-event-driven-mfe", [], false },
        { Vue, [], true },
    };

    [Theory]
    [MemberData(nameof(Shadcn))]
    public void Every_shadcn_project_has_a_skeleton_and_an_empty_state(string profile, string[] overrides, bool vue)
    {
        var files = Files(profile, overrides);

        if (vue)
        {
            files.Keys.ShouldContain("web/app/src/components/ui/skeleton/Skeleton.vue");
            files.Keys.ShouldContain("web/app/src/components/ui/empty-state/EmptyState.vue");
            files.Keys.ShouldContain("web/app/src/components/ui/empty-state/index.ts");
            files.Keys.ShouldContain("web/app/src/components/ui/empty-state/EmptyState.test.ts");
        }
        else
        {
            files.Keys.ShouldContain("web/app/src/components/ui/skeleton.tsx");
            files.Keys.ShouldContain("web/app/src/components/ui/empty-state.tsx");
            files.Keys.ShouldContain("web/app/src/components/ui/empty-state.test.tsx");
        }
    }

    [Theory]
    [MemberData(nameof(Shadcn))]
    public void The_table_shows_skeleton_rows_while_loading_and_its_empty_state_when_it_has_no_rows(string profile, string[] overrides, bool vue)
    {
        var files = Files(profile, overrides);

        var table = files[vue ? "web/app/src/components/ui/DataTable.vue" : "web/app/src/components/ui/data-table.tsx"];
        table.ShouldContain("aria-busy");
        table.ShouldContain("<Skeleton");
        table.ShouldContain("Loading…");
        table.ShouldContain(vue ? "<slot name=\"empty\"" : "empty?: ReactNode");
        files.Keys.ShouldContain(vue ? "web/app/src/components/ui/DataTable.axe.test.ts" : "web/app/src/components/ui/data-table.axe.test.tsx");
    }

    /// <summary>Out of scope (the PR says why): these keep the table they had, and get no empty state.</summary>
    [Theory]
    [InlineData("web/app/src/components/ui/data-table.tsx", React, "frontend.ui-library=radix-headless")]
    [InlineData("web/app/src/components/ui/data-table.tsx", React, "frontend.ui-library=mui", "frontend.styling=css-modules")]
    [InlineData("web/app/src/components/ui/DataTable.vue", Vue, "frontend.ui-library=reka-ui")]
    public void Other_ui_libraries_keep_the_table_they_had(string path, string profile, params string[] overrides)
    {
        var files = Files(profile, overrides);

        files[path].ShouldNotContain("aria-busy");
        files.Keys.ShouldNotContain(file => file.Contains("empty-state", StringComparison.OrdinalIgnoreCase));
        files.Keys.ShouldNotContain(file => file.Contains(".axe.test.", StringComparison.Ordinal) && file.Contains("components/ui/", StringComparison.Ordinal));
    }

    [Fact]
    public void With_ag_grid_the_grid_is_unchanged_and_the_empty_state_is_still_there()
    {
        var files = Files(React, "frontend.data-grid=ag-grid");

        files["web/app/src/components/ui/data-table.tsx"].ShouldNotContain("<Skeleton");
        files.Keys.ShouldNotContain("web/app/src/components/ui/data-table.axe.test.tsx");
        files.Keys.ShouldContain("web/app/src/components/ui/empty-state.tsx");
    }

    /// <summary>
    /// The web app builds, lints and formats, and its tests check the skeleton rows, the empty state in the table and on
    /// its own, and the table with axe while it loads, empty and with rows, in the light and the dark theme.
    /// </summary>
    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_loading_and_empty_states_build_and_their_tests_pass(string profile)
    {
        var vue = profile == Vue;
        var table = vue ? "src/components/ui/DataTable" : "src/components/ui/data-table";
        var emptyState = vue ? "src/components/ui/empty-state/EmptyState.test.ts" : "src/components/ui/empty-state.test.tsx";
        var test = vue ? "ts" : "tsx";
        using var project = new GeneratedProject(profile);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        string[] passed =
        [
            $"{table}.test.{test} > the table shows a header and one row per item",
            $"{table}.test.{test} > while its query is pending the table is busy, says it is loading and shows skeleton rows in its shape",
            $"{table}.test.{test} > a table without rows shows its empty state in place of them",
            $"{table}.test.{test} > a table with rows shows no empty state",
            $"{emptyState} > the empty state says what is missing, offers its action and hides its icon from screen readers",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find while it loads in the light theme",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find while it loads in the dark theme",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find when it is empty in the light theme",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find when it is empty in the dark theme",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find with rows in the light theme",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find with rows in the dark theme",
        ];
        foreach (var name in passed)
        {
            plain.ShouldContain($"✓ {name}", customMessage: testOutput);
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

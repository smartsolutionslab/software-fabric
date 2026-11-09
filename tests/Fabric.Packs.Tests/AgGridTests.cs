using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.data-grid.ag-grid (#274): AG Grid's community edition in place of TanStack Table, behind the same
/// <c>DataTable</c> and <c>columnsOf&lt;Row&gt;()</c>, so a screen and the dashboards' widget view are written the same
/// way with either grid, in React and in Vue.
/// </summary>
public sealed partial class AgGridTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string WithReactDashboards = "saas-ai-react";
    private const string VueDashboards = "frontend.dashboards=user-editable";
    private const string AgGrid = "frontend.data-grid=ag-grid";

    /// <summary>A screen a developer would write: a table of notes, one column read from a field, one computed.</summary>
    private const string ReactNotesTable = """
        import { DataTable } from '@/components/ui/data-table';
        import { columnsOf } from '@/components/ui/data-table-columns';

        interface Note {
          text: string;
          stars: number;
        }

        const notes = columnsOf<Note>();
        const columns = notes.columns([
          notes.accessor('text', { header: 'Text' }),
          notes.accessor((note) => '*'.repeat(note.stars), { id: 'rating', header: 'Rating' }),
        ]);
        const data: Note[] = [
          { text: 'first', stars: 2 },
          { text: 'second', stars: 3 },
        ];

        export function NotesTable() {
          return <DataTable columns={columns} data={data} />;
        }
        """;

    private const string ReactNotesTableTest = """
        import { cleanup, render, screen } from '@testing-library/react';
        import { afterEach, expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import { NotesTable } from './NotesTable';

        // Vitest runs without globals, so Testing Library cannot register its own cleanup.
        afterEach(cleanup);

        test('the notes grid has a header per column, a cell per field and computed value, and no axe violations', async () => {
          const { container } = render(<NotesTable />);

          expect(await screen.findByRole('columnheader', { name: 'Rating' })).toBeInTheDocument();
          expect(screen.getByRole('columnheader', { name: 'Text' })).toBeInTheDocument();
          expect(await screen.findByRole('gridcell', { name: 'second' })).toBeInTheDocument();
          expect(screen.getByRole('gridcell', { name: '***' })).toBeInTheDocument();
          expect(await axe(container)).toHaveNoViolations();
        });
        """;

    private const string VueNotesTable = """
        <script setup lang="ts">
        import DataTable from '@/components/ui/DataTable.vue';
        import { columnsOf } from '@/components/ui/data-table-columns';

        interface Note {
          text: string;
          stars: number;
        }

        const notes = columnsOf<Note>();
        const columns = notes.columns([
          notes.accessor('text', { header: 'Text' }),
          notes.accessor((note) => '*'.repeat(note.stars), { id: 'rating', header: 'Rating' }),
        ]);
        const data: Note[] = [
          { text: 'first', stars: 2 },
          { text: 'second', stars: 3 },
        ];
        </script>

        <template>
          <DataTable :columns="columns" :data="data" />
        </template>
        """;

    private const string VueNotesTableTest = """
        import { render, screen } from '@testing-library/vue';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import NotesTable from './NotesTable.vue';

        test('the notes grid has a header per column, a cell per field and computed value, and no axe violations', async () => {
          const { container } = render(NotesTable);

          expect(await screen.findByRole('columnheader', { name: 'Rating' })).toBeInTheDocument();
          expect(screen.getByRole('columnheader', { name: 'Text' })).toBeInTheDocument();
          expect(await screen.findByRole('gridcell', { name: 'second' })).toBeInTheDocument();
          expect(screen.getByRole('gridcell', { name: '***' })).toBeInTheDocument();
          expect(await axe(container)).toHaveNoViolations();
        });
        """;

    private const string OtherGrids = """
        import { useTable } from '@tanstack/react-table';
        import { useTable as useVueTable } from '@tanstack/vue-table';

        export const others = [useTable, useVueTable];
        """;

    /// <summary>
    /// Runs the data table's test and then <see cref="AfterTeardown"/> in one worker, in that order and not isolated, so
    /// the second file can stand in for jsdom's teardown right after the first.
    /// </summary>
    private const string AfterTeardownConfig = """
        import { defineConfig, mergeConfig } from 'vitest/config';
        import { BaseSequencer, type TestSpecification } from 'vitest/node';
        import base from './vite.config.ts';

        class ByName extends BaseSequencer {
          async sort(files: TestSpecification[]) {
            return [...files].sort((a, b) => a.moduleId.localeCompare(b.moduleId));
          }
        }

        export default mergeConfig(base, defineConfig({ test: { isolate: false, maxWorkers: 1, sequence: { sequencer: ByName } } }));
        """;

    /// <summary>
    /// Takes window away for longer than AG Grid's 500 ms scroll refresh timer, as jsdom's teardown does, so a timer a
    /// grid left behind fires against a torn-down environment and fails the run every time rather than now and then (#414).
    /// </summary>
    private const string AfterTeardown = """
        import { expect, test } from 'vitest';

        test('no timer from an earlier file needs window after it is gone', async () => {
          const window = globalThis.window;
          Reflect.deleteProperty(globalThis, 'window');
          try {
            await new Promise((resolve) => setTimeout(resolve, 1000));
          } finally {
            globalThis.window = window;
          }
          expect(globalThis.window).toBe(window);
        });
        """;

    [Fact]
    public void In_react_ag_grid_replaces_tanstack_table_in_the_dependencies_the_data_table_the_rules_the_adr_and_the_lint_rule()
    {
        var files = Files(React, AgGrid);

        var package = files["web/app/package.json"];
        package.ShouldContain("\"ag-grid-community\": \"36.3.0\"");
        package.ShouldContain("\"ag-grid-react\": \"36.3.0\"");
        package.ShouldNotContain("ag-grid-enterprise");
        package.ShouldNotContain("@tanstack/react-table");
        files["web/app/src/components/ui/data-table.tsx"].ShouldContain("import { AgGridReact } from 'ag-grid-react';");
        files["web/app/src/components/ui/data-table-columns.ts"].ShouldContain("export const columnsOf = <TRow>(): Columns<TRow> => ({");
        files["web/app/src/components/ui/data-table-columns.ts"].ShouldContain("export const modules: Module[] = [ClientSideRowModelModule];");
        files.Keys.ShouldContain("web/app/src/components/ui/data-table.test.tsx");
        files.Keys.ShouldNotContain("web/app/src/components/ui/DataTable.vue");
        files["web/app/eslint.config.js"].ShouldContain("{ group: ['@tanstack/*-table'], message: 'Use DataTable from src/components/ui (docs/adr).' },");
        files["web/app/eslint.config.js"].ShouldNotContain("'ag-grid-*'");
        files["AGENTS.md"].ShouldContain("## Data grid: AG Grid (community)");
        files["AGENTS.md"].ShouldNotContain("TanStack Table");
        files.Keys.ShouldContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-data-grid-ag-grid.md", StringComparison.Ordinal));
        files.Keys.ShouldNotContain(path => path.EndsWith("-frontend-data-grid-tanstack-table.md", StringComparison.Ordinal));
        WebSources(files).ShouldNotContain(source => source.Value.Contains("@tanstack/react-table", StringComparison.Ordinal), "no web source imports TanStack Table");
    }

    [Fact]
    public void In_vue_ag_grid_replaces_tanstack_table_with_the_vue_component_and_no_react_package()
    {
        var files = Files(Vue, AgGrid);

        var package = files["web/app/package.json"];
        package.ShouldContain("\"ag-grid-community\": \"36.3.0\"");
        package.ShouldContain("\"ag-grid-vue3\": \"36.3.0\"");
        package.ShouldNotContain("ag-grid-react");
        package.ShouldNotContain("@tanstack/vue-table");
        files["web/app/src/components/ui/DataTable.vue"].ShouldContain("import { AgGridVue } from 'ag-grid-vue3';");
        files.Keys.ShouldContain("web/app/src/components/ui/DataTable.test.ts");
        files.Keys.ShouldContain("web/app/src/components/ui/data-table-columns.ts");
        files.Keys.Where(path => path.EndsWith(".tsx", StringComparison.Ordinal)).ShouldBeEmpty();
        files["AGENTS.md"].ShouldContain("## Data grid: AG Grid (community)");
        files["AGENTS.md"].ShouldContain("`DataTable` (`@/components/ui/DataTable.vue`)");
    }

    private const string BoundTheme = """
        export const theme = themeQuartz.withParams({
          fontFamily: 'inherit',
          backgroundColor: 'var(--background)',
          foregroundColor: 'var(--foreground)',
          borderColor: 'var(--border)',
          headerBackgroundColor: 'var(--muted)',
          accentColor: 'var(--primary)',
        });
        """;

    [Theory]
    [InlineData(React, "web/app/src/components/ui/data-table.tsx", "theme={theme}")]
    [InlineData(Vue, "web/app/src/components/ui/DataTable.vue", ":theme=\"theme\"")]
    public void With_shadcn_the_grid_is_quartz_bound_to_the_theme_variables_of_index_css(string profile, string table, string themeProperty)
    {
        var files = Files(profile, AgGrid);

        files["web/app/src/components/ui/data-table-columns.ts"].ShouldContain(BoundTheme);
        files[table].ShouldContain(themeProperty);
        files["AGENTS.md"].ShouldContain("The grid's colours, borders and font come from the theme variables in `src/index.css`");
        Adr(files).ShouldContain("Quartz theme bound to the theme variables in `src/index.css`");
    }

    [Theory]
    [InlineData(React, "radix-headless", "web/app/src/components/ui/data-table.tsx", "theme={theme}")]
    [InlineData(Vue, "reka-ui", "web/app/src/components/ui/DataTable.vue", ":theme=\"theme\"")]
    public void With_another_ui_library_the_grid_is_quartz_as_shipped_and_the_adr_records_the_exception(string profile, string library, string table, string themeProperty)
    {
        // Stands in for the library only where it has no pack; radix-headless and reka-ui have theirs, which TryAdd keeps.
        using var probe = new ProbePack($"frontend.ui-library.{library}", new Dictionary<string, string>());
        var packs = Repository.Packs.Packs.ToDictionary();
        packs.TryAdd(probe.Manifest.Option, probe.Manifest);
        var generated = Generator.New(Repository.Catalog, packs, "shop", profile, [AgGrid, $"frontend.ui-library={library}"]);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        var columns = files["web/app/src/components/ui/data-table-columns.ts"];
        columns.ShouldContain("export const theme = themeQuartz;");
        columns.ShouldNotContain("var(--");
        files[table].ShouldContain(themeProperty);
        files["AGENTS.md"].ShouldContain("The grid uses AG Grid's Quartz theme as shipped");
        Adr(files).ShouldContain("the exception to changing colours in one place");
    }

    [Fact]
    public void A_react_screen_with_an_ag_grid_table_builds_lints_passes_its_tests_and_lint_refuses_tanstack_table()
    {
        using var project = new GeneratedProject(React, AgGrid);
        project.Write("web/app/src/pages/NotesTable.tsx", ReactNotesTable);
        project.Write("web/app/src/pages/NotesTable.test.tsx", ReactNotesTableTest);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/components/ui/data-table.test.tsx > the table shows a header and one row per item", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/NotesTable.test.tsx > the notes grid has a header per column, a cell per field and computed value, and no axe violations", customMessage: testOutput);
        NoGridTimerOutlivesTheTest(project, "src/components/ui/data-table.test.tsx");
        LintRefusesTanStackTable(project);
    }

    [Fact]
    public void A_vue_screen_with_an_ag_grid_table_builds_lints_passes_its_tests_and_lint_refuses_tanstack_table()
    {
        using var project = new GeneratedProject(Vue, AgGrid);
        project.Write("web/app/src/pages/NotesTable.vue", VueNotesTable);
        project.Write("web/app/src/pages/NotesTable.test.ts", VueNotesTableTest);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/components/ui/DataTable.test.ts > the table shows a header and one row per item", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/NotesTable.test.ts > the notes grid has a header per column, a cell per field and computed value, and no axe violations", customMessage: testOutput);
        NoGridTimerOutlivesTheTest(project, "src/components/ui/DataTable.test.ts");
        LintRefusesTanStackTable(project);
    }

    [Fact]
    public void With_ag_grid_the_react_dashboards_web_app_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(WithReactDashboards, AgGrid);

        var testOutput = InstallBuildLintAndTest(project);

        Escapes().Replace(testOutput, string.Empty).ShouldContain("✓ src/dashboards/DashboardEditor.test.tsx", customMessage: testOutput);
        FormatIsClean(project);
    }

    [Fact]
    public void With_ag_grid_the_vue_dashboards_web_app_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Vue, VueDashboards, AgGrid);

        var testOutput = InstallBuildLintAndTest(project);

        Escapes().Replace(testOutput, string.Empty).ShouldContain("✓ src/dashboards/DashboardEditor.test.ts", customMessage: testOutput);
        FormatIsClean(project);
    }

    /// <summary>Runs the data table's test with <see cref="AfterTeardown"/> after it: the grid's timers must end with the test.</summary>
    private static void NoGridTimerOutlivesTheTest(GeneratedProject project, string dataTableTest)
    {
        project.Write("web/app/after-teardown.config.ts", AfterTeardownConfig);
        project.Write("web/app/src/zz-after-teardown.test.ts", AfterTeardown);
        var (exitCode, output) = Shell.Run(
            Path.Combine(project.ProjectDirectory, "web", "app"),
            "pnpm",
            "exec",
            "vitest",
            "run",
            "--config",
            "after-teardown.config.ts",
            dataTableTest,
            "src/zz-after-teardown.test.ts");
        output.ShouldNotContain("window is not defined", customMessage: output);
        exitCode.ShouldBe(0, output);
    }

    private static void LintRefusesTanStackTable(GeneratedProject project)
    {
        project.Write("web/app/src/pages/OtherGrids.ts", OtherGrids);
        var (exitCode, lintOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, lintOutput);
        lintOutput.ShouldContain("'@tanstack/react-table' import is restricted from being used by a pattern. Use DataTable from src/components/ui (docs/adr)");
        lintOutput.ShouldContain("'@tanstack/vue-table' import is restricted from being used by a pattern. Use DataTable from src/components/ui (docs/adr)");
    }

    private static void FormatIsClean(GeneratedProject project)
    {
        var (exitCode, format) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, format);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static string Adr(Dictionary<string, string> files) =>
        files.Single(file => file.Key.EndsWith("-frontend-data-grid-ag-grid.md", StringComparison.Ordinal)).Value;

    private static IEnumerable<KeyValuePair<string, string>> WebSources(Dictionary<string, string> files) =>
        files.Where(file => file.Key.StartsWith("web/app/src/", StringComparison.Ordinal) && Regex.IsMatch(file.Key, @"\.(ts|tsx|vue)$"));

    /// <summary>Installs, builds and lints the web workspace, runs the app's tests, and returns their verbose output.</summary>
    private static string InstallBuildLintAndTest(GeneratedProject project)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(Path.Combine(web, "app"), "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        return testOutput;
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #159: the Vue branches of the shared sign-in, table and chart packs (ADR-0006). Sign-in is a hand-written
/// <c>useAuth()</c> and <c>SignedIn.vue</c> over oidc-client-ts's <c>UserManager</c>, with no Vue OIDC dependency
/// (owner decision: https://github.com/smartsolutionslab/software-fabric/issues/23#issuecomment-6641134974); tables are
/// @tanstack/vue-table and charts vue-echarts. The packs #160 writes (forms, components, styling) stand in.
/// </summary>
public sealed partial class VueSharedPacksTests
{
    private const string Profile = "saas-standard-vue";

    /// <summary>A screen a developer would write: a table of notes from DataTable's column helper.</summary>
    private const string NotesTable = """
        <script setup lang="ts">
        import DataTable from '@/components/ui/DataTable.vue';
        import { columnsOf } from '@/components/ui/data-table-columns';

        interface Note {
          text: string;
        }

        const notes = columnsOf<Note>();
        const columns = notes.columns([notes.accessor('text', { header: 'Text' })]);
        const data: Note[] = [{ text: 'first note' }, { text: 'second note' }];
        </script>

        <template>
          <DataTable :columns="columns" :data="data" />
        </template>
        """;

    private const string NotesTableTest = """
        import { render, screen } from '@testing-library/vue';
        import { expect, test } from 'vitest';
        import NotesTable from './NotesTable.vue';

        test('the notes table has a header and a cell per note', () => {
          render(NotesTable);

          expect(screen.getByRole('columnheader', { name: 'Text' })).toBeInTheDocument();
          expect(screen.getByRole('cell', { name: 'first note' })).toBeInTheDocument();
          expect(screen.getByRole('cell', { name: 'second note' })).toBeInTheDocument();
        });
        """;

    /// <summary>A chart a developer would draw: its option is type-checked against ECharts' and it has a name.</summary>
    private const string NotesChart = """
        <script setup lang="ts">
        import type { EChartsOption } from 'echarts';
        import DataChart from '@/components/DataChart.vue';

        const option: EChartsOption = {
          xAxis: { type: 'category', data: ['Monday', 'Tuesday'] },
          yAxis: { type: 'value' },
          series: [{ type: 'bar', data: [3, 5] }],
        };
        </script>

        <template>
          <DataChart :option="option" label="Notes per day" />
        </template>
        """;

    [Fact]
    public void A_vue_project_signs_in_tabulates_and_charts_without_a_react_file_or_package()
    {
        var files = Files();

        files.Keys.Where(path => path.EndsWith(".tsx", StringComparison.Ordinal)).ShouldBeEmpty();
        files.Keys.ShouldContain("web/app/src/auth/useAuth.ts");
        files.Keys.ShouldContain("web/app/src/auth/SignedIn.vue");
        files.Keys.ShouldContain("web/app/src/providers/auth.ts");
        files.Keys.ShouldContain("web/app/src/components/ui/DataTable.vue");
        files.Keys.ShouldContain("web/app/src/components/DataChart.vue");

        var package = files["web/app/package.json"];
        package.ShouldNotContain("react");
        package.ShouldContain("\"oidc-client-ts\": \"3.5.0\"");
        package.ShouldContain("\"@tanstack/vue-table\": \"9.3.1\"");
        package.ShouldContain("\"echarts\": \"6.1.0\"");
        package.ShouldContain("\"vue-echarts\": \"8.3.2\"");

        files["AGENTS.md"].ShouldNotContain("react-oidc-context");
        files["AGENTS.md"].ShouldContain("useAuth()` from `@/auth/useAuth`");
    }

    [Fact]
    public void The_vue_sign_in_and_table_tests_pass_and_a_screen_with_a_table_and_a_chart_builds_lints_and_passes_its_test()
    {
        using var project = new GeneratedProject(Profile);
        project.Write("web/app/src/pages/NotesTable.vue", NotesTable);
        project.Write("web/app/src/pages/NotesTable.test.ts", NotesTableTest);
        project.Write("web/app/src/pages/NotesChart.vue", NotesChart);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        // CI forces colour, which puts escape codes between the parts of a reported test's name.
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/auth/SignedIn.test.ts > an anonymous visitor is sent to the identity provider", customMessage: testOutput);
        plain.ShouldContain("✓ src/auth/SignedIn.test.ts > a signed-in user sees who they are and the application", customMessage: testOutput);
        plain.ShouldContain("✓ src/auth/SignedIn.test.ts > coming back from the identity provider completes the sign-in and drops its parameters from the address", customMessage: testOutput);
        plain.ShouldContain("✓ src/auth/SignedIn.test.ts > a failed sign-in is shown, not retried", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/DataTable.test.ts > the table shows a header and one row per item", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/NotesTable.test.ts > the notes table has a header and a cell per note", customMessage: testOutput);
    }

    [Fact]
    public void Importing_another_chart_or_grid_library_fails_lint_in_vue()
    {
        using var project = new GeneratedProject(Profile);
        project.Write("web/app/src/pages/OtherLibraries.ts", """
            import { AgGridVue } from 'ag-grid-vue3';
            import { Bar } from 'vue-chartjs';

            export const others = [AgGridVue, Bar];
            """);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'ag-grid-vue3' import is restricted from being used by a pattern. Use DataTable from src/components/ui (docs/adr)");
        output.ShouldContain("'vue-chartjs' import is restricted from being used by a pattern. Use DataChart from src/components/DataChart.vue (docs/adr)");
    }

    [Fact]
    public void Showing_and_hiding_by_role_or_claim_in_vue_fails_loudly_until_it_is_written()
    {
        var files = Files("backend.authorization=roles,claims");
        files.Keys.ShouldNotContain("web/app/src/auth/useHasRole.ts");
        files.Keys.ShouldNotContain("web/app/src/auth/useHasClaim.ts");

        using var project = new GeneratedProject(Profile, "backend.authorization=roles,claims");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        var (exitCode, output) = Shell.Run(web, "pnpm", "test");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("The oidc-client-ts pack shows and hides by role or claim (RequirePermission, useHasRole, useHasClaim) for React only so far");
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        using var standIns = new StandIns();
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);
        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

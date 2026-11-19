using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #700: with shadcn or shadcn-vue, TanStack Table's <c>DataTable</c> keeps its header in view, has a density toggle
/// (comfortable, compact) and a control that hides and shows its columns, shows a row's actions in its last cell on
/// hover or focus, and says below the rows which of how many it shows when it is given its <c>PagedResult</c>; the
/// module page's sample list gives it one. Keyboard and screen-reader behaviour are tested in the generated project,
/// and axe checks the new controls. The other UI libraries and AG Grid keep the table they had.
/// </summary>
public sealed partial class DataTableDetailsTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string TanStackRouter = "frontend.routing=tanstack-router";

    [Theory]
    [InlineData(React, false)]
    [InlineData(Vue, true)]
    public void The_shadcn_table_registers_column_visibility_and_takes_actions_and_a_page(string profile, bool vue)
    {
        var files = Files(profile);

        files["web/app/src/components/ui/data-table-columns.ts"].ShouldContain("columnVisibilityFeature");
        var table = files[vue ? "web/app/src/components/ui/DataTable.vue" : "web/app/src/components/ui/data-table.tsx"];
        table.ShouldContain("sticky top-0");
        table.ShouldContain("aria-label=\"Density\"");
        table.ShouldContain("Visible columns");
        table.ShouldContain("group-focus-within:opacity-100");
        table.ShouldContain("paging?:");
        table.ShouldContain("aria-describedby");
    }

    [Theory]
    [InlineData(React, "web/app/src/modules/billing/BillingPage.tsx", "paging={page}")]
    [InlineData(Vue, "web/app/src/modules/billing/BillingPage.vue", ":paging=\"page\"")]
    public void The_module_page_gives_its_sample_list_the_page_the_host_answered(string profile, string path, string paging)
    {
        var files = Files(profile, withModule: true);

        files[path].ShouldContain(paging);
    }

    /// <summary>#826: the TanStack Router module page gives it the page too, whichever client fetched it.</summary>
    [Theory]
    [InlineData(React)]
    [InlineData("lean-crud")]
    [InlineData("lean-crud", "frontend.api-client=openapi-fetch")]
    public void With_TanStack_Router_the_module_page_gives_its_sample_list_the_page_the_host_answered(string profile, params string[] overrides)
    {
        var files = Files(profile, true, [TanStackRouter, .. overrides]);

        files["web/app/src/modules/billing/BillingPage.tsx"].ShouldContain("paging={page}");
        files["web/app/src/modules/billing/BillingPageSamples.test.tsx"].ShouldContain("Showing 1–2 of 42");
    }

    /// <summary>Out of scope: these keep the table and columns they had.</summary>
    [Theory]
    [InlineData("web/app/src/components/ui/data-table.tsx", React, "frontend.ui-library=radix-headless")]
    [InlineData("web/app/src/components/ui/data-table.tsx", React, "frontend.ui-library=mui", "frontend.styling=css-modules")]
    [InlineData("web/app/src/components/ui/DataTable.vue", Vue, "frontend.ui-library=reka-ui")]
    [InlineData("web/app/src/components/ui/DataTable.vue", Vue, "frontend.ui-library=vuetify")]
    public void Other_ui_libraries_keep_the_table_they_had(string path, string profile, params string[] overrides)
    {
        var files = Files(profile, false, overrides);

        files[path].ShouldNotContain("aria-label=\"Density\"");
        files[path].ShouldNotContain("paging");
        files["web/app/src/components/ui/data-table-columns.ts"].ShouldNotContain("columnVisibilityFeature");
    }

    /// <summary>
    /// The web app builds, lints and formats, and its tests check the sticky header, the density toggle and the columns
    /// control from the keyboard, the row actions, the page footer as the table's description, the module page's footer,
    /// and axe with the actions, the footer and the columns control open, in both themes.
    /// </summary>
    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_table_details_build_and_their_tests_pass(string profile)
    {
        var vue = profile == Vue;
        var table = vue ? "src/components/ui/DataTable" : "src/components/ui/data-table";
        var test = vue ? "ts" : "tsx";
        using var project = new GeneratedProject(profile);
        project.AddModule("billing");
        var plain = BuildAndTest(project);

        string[] passed =
        [
            $"{table}.test.{test} > the header stays in view while the rows scroll under it",
            $"{table}.test.{test} > the density toggle switches the rows between comfortable and compact from the keyboard and says which is on",
            $"{table}.test.{test} > the columns control hides and shows columns from the keyboard and always keeps one",
            $"{table}.test.{test} > a row's actions sit in its last cell, show on hover or focus, and are reached with the keyboard",
            $"{table}.test.{test} > with a page of a longer list the table says which rows it shows of how many, and screen readers hear it with the table",
            $"{table}.test.{test} > the table says nothing of its page while it loads or when it has no rows",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find with actions, a page and columns open in the light theme",
            $"{table}.axe.test.{test} > the table has no accessibility violations axe can find with actions, a page and columns open in the dark theme",
            $"src/modules/billing/BillingPageSamples.test.{test} > below the samples the page says which of how many it shows, from the page the host answered",
        ];
        foreach (var name in passed)
        {
            plain.ShouldContain($"✓ {name}");
        }
    }

    /// <summary>Installs, builds, lints, checks the format and runs the tests; returns the test output without colours.</summary>
    internal static string BuildAndTest(GeneratedProject project)
    {
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
        return Escapes().Replace(testOutput, string.Empty);
    }

    private static Dictionary<string, string> Files(string profile, bool withModule = false, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);
        if (withModule)
        {
            var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
            var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, "billing", []);
            addition.Errors.ShouldBeEmpty();
            foreach (var file in addition.Created)
            {
                files[file.Path] = file.Content;
            }
        }

        return files;
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

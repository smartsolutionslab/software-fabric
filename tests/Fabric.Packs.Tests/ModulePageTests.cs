using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #734, PR 2: a module's page lists its sample (PR 1) in <c>DataTable</c>, with skeleton rows while it loads and an
/// <c>EmptyState</c> with the create action when it has none, and creates one with a form in the project's forms
/// library, shown in the list at once and taken out again when the server refuses it. The result is a toast through
/// <c>notify</c> where the app shell is, and a message beside the form elsewhere. Built for React single-page apps
/// with shadcn and the RTK Query or Orval client; every other page keeps what it had.
/// </summary>
public sealed partial class ModulePageTests
{
    private const string Module = "web/app/src/modules/billing/";
    private const string Page = Module + "BillingPage.tsx";
    private const string Hook = Module + "useBillingSamples.ts";
    private const string Form = Module + "CreateBillingSampleForm.tsx";

    public static TheoryData<string, string[]> Built => new()
    {
        { "saas-standard-react", [] },
        { "lean-crud", [] },
        { "saas-strict-react", ["frontend.routing=tanstack-router", "frontend.forms=tanstack-form", "frontend.validation=valibot"] },
        { "lean-crud", ["frontend.data-grid=ag-grid", "backend.auth=none"] },
    };

    [Theory]
    [MemberData(nameof(Built))]
    public void The_page_lists_the_samples_in_the_table_and_creates_one_with_the_form(string profile, string[] overrides)
    {
        var files = WithBilling(profile, overrides);

        files[Page].ShouldContain("useBillingSamples()");
        files[Page].ShouldContain("useCreateBillingSample()");
        files[Page].ShouldContain("<DataTable");
        files[Page].ShouldContain("<EmptyState");
        files[Page].ShouldContain("<CreateBillingSampleForm");
        files.Keys.ShouldContain(Form);
        files.Keys.ShouldContain(Module + "BillingPageSamples.test.tsx");
        files.Keys.ShouldContain(Module + "useBillingSamples.test.tsx");
        files.Keys.ShouldContain("web/app/e2e/billing-samples.spec.ts");
    }

    [Fact]
    public void With_the_rtk_query_client_a_created_sample_is_in_the_cached_list_until_the_server_refuses_it()
    {
        var hook = WithBilling("saas-standard-react")[Hook];

        hook.ShouldContain("onQueryStarted");
        hook.ShouldContain("updateQueryData('getBillingSamples'");
        hook.ShouldContain("patch.undo()");
    }

    [Fact]
    public void With_the_orval_client_a_created_sample_is_in_the_cached_list_until_the_server_refuses_it()
    {
        var hook = WithBilling("lean-crud")[Hook];

        hook.ShouldContain("onMutate");
        hook.ShouldContain("setQueryData(");
        hook.ShouldContain("onError");
    }

    [Theory]
    [InlineData("react-hook-form", "zod", "zodResolver")]
    [InlineData("react-hook-form", "valibot", "valibotResolver")]
    [InlineData("tanstack-form", "zod", "@tanstack/react-form")]
    [InlineData("tanstack-form", "valibot", "@tanstack/react-form")]
    public void The_form_is_the_projects_forms_library_with_its_validation_library(string forms, string validation, string expected)
    {
        var form = WithBilling("saas-standard-react", $"frontend.forms={forms}", $"frontend.validation={validation}")[Form];

        form.ShouldContain(expected);
        form.ShouldContain(validation == "zod" ? "from 'zod'" : "from 'valibot'");
    }

    /// <summary>The shell's toasts where the shell is: with shadcn, react-router's (#695) and TanStack Router's (#736).</summary>
    [Theory]
    [InlineData("frontend.routing=react-router", true)]
    [InlineData("frontend.routing=tanstack-router", true)]
    public void The_result_is_a_toast_where_the_shell_is_and_a_message_beside_the_form_elsewhere(string routing, bool shell)
    {
        var page = WithBilling("saas-standard-react", routing)[Page];

        page.Contains("notify.success(", StringComparison.Ordinal).ShouldBe(shell);
        page.Contains("role={message.role}", StringComparison.Ordinal).ShouldBe(!shell);
    }

    [Fact]
    public void With_ag_grid_the_page_draws_the_skeleton_rows_and_the_empty_state_beside_the_grid()
    {
        var page = WithBilling("lean-crud", "frontend.data-grid=ag-grid")[Page];

        page.ShouldContain("<Skeleton");
        page.ShouldContain("aria-busy");
        page.ShouldNotContain("loading={isLoading}");
    }

    /// <summary>Out of scope, and keep the page they had: the UI libraries without the shell's components, micro-frontends (Vue and openapi-fetch: ModulePageVueTests).</summary>
    [Theory]
    [InlineData("saas-standard-react", "frontend.ui-library=mui", "frontend.styling=css-modules")]
    [InlineData("saas-standard-react", "frontend.ui-library=radix-headless")]
    [InlineData("industrial-event-driven-mfe")]
    public void Elsewhere_the_page_is_unchanged(string profile, params string[] overrides)
    {
        var files = WithBilling(profile, overrides);

        files.Keys.ShouldNotContain(Hook);
        files.Keys.ShouldNotContain(Form);
        files.Keys.ShouldNotContain("web/app/e2e/billing-samples.spec.ts");
    }

    [Fact]
    public void The_pages_data_file_has_an_example_with_samples_and_an_empty_one_and_axe_checks_the_three_states()
    {
        var files = WithBilling("saas-standard-react");

        var data = files[Module + "BillingPage.data.ts"];
        data.ShouldContain("export const examples");
        data.ShouldContain("useGetBillingSamplesQuery: {");
        data.ShouldContain("export const emptyExamples");
        var axe = files[Module + "BillingPage.axe.test.tsx"];
        axe.ShouldContain("emptyExamples");
        axe.ShouldContain("when it has nothing to show");
        axe.ShouldContain("with its example data");
    }

    [Fact]
    public void The_dashboards_and_assistant_pages_keep_what_they_show()
    {
        var files = Generate("saas-ai-react");

        files["web/app/src/modules/dashboards/DashboardsPage.tsx"].ShouldNotContain("Samples");
        files["web/app/src/modules/assistant/AssistantPage.tsx"].ShouldNotContain("Samples");
        files.Keys.ShouldNotContain("web/app/src/modules/assistant/useAssistantSamples.ts");
    }

    /// <summary>
    /// The web app builds, lints and formats with the page, and its tests show the list, the empty state with the create
    /// action, the skeleton while it loads, a created sample at once and gone again when refused, and axe in the three states.
    /// </summary>
    [Theory]
    [MemberData(nameof(Built))]
    public void The_page_builds_and_its_tests_pass(string profile, string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
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
            "BillingPageSamples.test.tsx > the page lists the samples by name",
            "BillingPageSamples.test.tsx > while the samples load the page shows skeleton rows",
            "BillingPageSamples.test.tsx > without samples the page says so and its action leads to the form",
            "BillingPageSamples.test.tsx > a created sample is reported",
            "BillingPageSamples.test.tsx > a sample the server refuses is reported as a failure",
            "BillingPageSamples.test.tsx > a blank name is refused before anything is sent",
            "useBillingSamples.test.tsx > a created sample is in the list before the server answers and stays once it is created",
            "useBillingSamples.test.tsx > a sample the server refuses is taken out of the list again",
            "BillingPage.axe.test.tsx > the Billing page has no accessibility violations axe can find",
            "BillingPage.axe.test.tsx > the Billing page passes axe when it has nothing to show",
            "BillingPage.axe.test.tsx > the Billing page passes axe with its example data",
        ];
        foreach (var name in passed)
        {
            plain.ShouldContain($"✓ src/modules/billing/{name}", customMessage: testOutput);
        }
    }

    private static Dictionary<string, string> WithBilling(string profile, params string[] overrides)
    {
        var files = Generate(profile, overrides);
        var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, "billing", []);
        addition.Errors.ShouldBeEmpty();
        foreach (var file in addition.Created)
        {
            files[file.Path] = file.Content;
        }

        return files;
    }

    private static Dictionary<string, string> Generate(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

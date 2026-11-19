using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #734, PR 3: the module page of PR 2 in a Vue single-page app with shadcn-vue, with the Orval or the openapi-fetch
/// client, through TanStack Query or a Pinia store; and in a React one with the openapi-fetch client. The same list in
/// <c>DataTable</c> with skeleton rows and an <c>EmptyState</c>, a create form in the project's forms library, the new
/// sample shown at once and taken out again when the host refuses it, and the result in the shell's toasts.
/// </summary>
public sealed partial class ModulePageVueTests
{
    private const string Module = "web/app/src/modules/billing/";
    private const string Page = Module + "BillingPage.vue";
    private const string Hook = Module + "useBillingSamples.ts";
    private const string Form = Module + "CreateBillingSampleForm.vue";
    private const string Vue = "saas-standard-vue";

    public static TheoryData<string, string[]> Built => new()
    {
        { Vue, [] },
        { Vue, ["frontend.state=pinia", "frontend.api-client=openapi-fetch", "frontend.forms=formkit", "frontend.validation=valibot"] },
        { Vue, ["frontend.api-client=openapi-fetch", "frontend.forms=tanstack-form", "frontend.data-grid=ag-grid"] },
        { Vue, ["frontend.state=pinia", "frontend.validation=valibot"] },
    };

    [Theory]
    [MemberData(nameof(Built))]
    public void The_vue_page_lists_the_samples_in_the_table_and_creates_one_with_the_form(string profile, string[] overrides)
    {
        var files = WithBilling(profile, overrides);

        files[Page].ShouldContain("useBillingSamples()");
        files[Page].ShouldContain("useCreateBillingSample()");
        files[Page].ShouldContain("<DataTable");
        files[Page].ShouldContain("<EmptyState");
        files[Page].ShouldContain("<CreateBillingSampleForm");
        files[Page].ShouldContain("notify.success(");
        files.Keys.ShouldContain(Form);
        files.Keys.ShouldContain(Module + "BillingPageSamples.test.ts");
        files.Keys.ShouldContain(Module + "useBillingSamples.test.ts");
        files.Keys.ShouldContain("web/app/e2e/billing-samples.spec.ts");
    }

    [Theory]
    [InlineData("frontend.api-client=orval")]
    [InlineData("frontend.api-client=openapi-fetch")]
    public void Through_tanstack_query_a_created_sample_is_in_the_cached_list_until_the_server_refuses_it(string client)
    {
        var hook = WithBilling(Vue, client)[Hook];

        hook.ShouldContain("onMutate");
        hook.ShouldContain("setQueryData(");
        hook.ShouldContain("onError");
    }

    [Theory]
    [InlineData("frontend.api-client=orval")]
    [InlineData("frontend.api-client=openapi-fetch")]
    public void Through_a_pinia_store_a_created_sample_is_in_the_list_until_the_server_refuses_it(string client)
    {
        var hook = WithBilling(Vue, "frontend.state=pinia", client)[Hook];

        hook.ShouldContain("defineStore(");
        hook.ShouldContain("page.value = before;");
    }

    [Theory]
    [InlineData("vee-validate", "toTypedSchema")]
    [InlineData("tanstack-form", "@tanstack/vue-form")]
    [InlineData("formkit", "schemaSubmit")]
    public void The_vue_form_is_the_projects_forms_library(string forms, string expected)
    {
        WithBilling(Vue, $"frontend.forms={forms}")[Form].ShouldContain(expected);
    }

    [Fact]
    public void With_ag_grid_the_vue_page_draws_the_skeleton_rows_and_the_empty_state_beside_the_grid()
    {
        var page = WithBilling(Vue, "frontend.data-grid=ag-grid")[Page];

        page.ShouldContain("<Skeleton");
        page.ShouldContain("aria-busy");
        page.ShouldNotContain(":loading=\"isLoading\"");
    }

    [Fact]
    public void With_the_openapi_fetch_client_the_react_page_lists_and_creates_the_samples_too()
    {
        var files = WithBilling("lean-crud", "frontend.api-client=openapi-fetch");

        files[Module + "BillingPage.tsx"].ShouldContain("useBillingSamples()");
        files[Hook].ShouldContain("api.POST('/billing/samples'");
        files[Hook].ShouldContain("onMutate");
        files.Keys.ShouldContain(Module + "BillingPage.data.ts");
    }

    [Fact]
    public void The_vue_pages_data_file_has_an_empty_example_and_axe_checks_the_three_states()
    {
        var files = WithBilling(Vue);

        files[Module + "BillingPage.data.ts"].ShouldContain("export const emptyExamples");
        var axe = files[Module + "BillingPage.axe.test.ts"];
        axe.ShouldContain("when it has nothing to show");
        axe.ShouldContain("with its example data");
    }

    /// <summary>Out of scope, and keep the page they had: the Vue UI libraries without the shell's components, micro-frontends.</summary>
    [Theory]
    [InlineData(Vue, "frontend.ui-library=vuetify", "frontend.styling=library-theme")]
    [InlineData(Vue, "frontend.ui-library=reka-ui")]
    [InlineData(Vue, "frontend.architecture=micro-frontends", "frontend.mfe-technique=native-federation", "frontend.design-system=tokens")]
    public void Elsewhere_the_vue_page_is_unchanged(string profile, params string[] overrides)
    {
        var files = WithBilling(profile, overrides);

        files.Keys.ShouldNotContain(Hook);
        files.Keys.ShouldNotContain(Form);
    }

    /// <summary>
    /// The web app builds, lints and formats with the page, and its tests show the list, the empty state with the create
    /// action, the skeleton while it loads, a created sample at once and gone again when refused, and axe in the three states.
    /// </summary>
    [Theory]
    [MemberData(nameof(Built))]
    [InlineData("lean-crud", new[] { "frontend.api-client=openapi-fetch" })]
    public void The_page_builds_and_its_tests_pass(string profile, string[] overrides)
    {
        var vue = profile == Vue;
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
        var test = vue ? "ts" : "tsx";
        string[] passed =
        [
            $"BillingPageSamples.test.{test} > the page lists the samples by name",
            $"BillingPageSamples.test.{test} > while the samples load the page shows skeleton rows",
            $"BillingPageSamples.test.{test} > without samples the page says so and its action leads to the form",
            $"BillingPageSamples.test.{test} > a created sample is reported",
            $"BillingPageSamples.test.{test} > a sample the server refuses is reported as a failure",
            $"BillingPageSamples.test.{test} > a blank name is refused before anything is sent",
            $"useBillingSamples.test.{test} > a created sample is in the list before the server answers and stays once it is created",
            $"useBillingSamples.test.{test} > a sample the server refuses is taken out of the list again",
            $"BillingPage.axe.test.{test} > the Billing page has no accessibility violations axe can find",
            $"BillingPage.axe.test.{test} > the Billing page passes axe when it has nothing to show",
            $"BillingPage.axe.test.{test} > the Billing page passes axe with its example data",
        ];
        foreach (var name in passed)
        {
            plain.ShouldContain($"✓ src/modules/billing/{name}", customMessage: testOutput);
        }
    }

    private static Dictionary<string, string> WithBilling(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);
        var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, "billing", []);
        addition.Errors.ShouldBeEmpty();
        foreach (var file in addition.Created)
        {
            files[file.Path] = file.Content;
        }

        return files;
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

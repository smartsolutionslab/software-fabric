using System.Text.Json.Nodes;
using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.dashboards: [user-editable]</c> (ADR-0015): users arrange and save widgets on dashboards of their own,
/// per user and tenant. The pack brings a <c>dashboards</c> module (CRUD, logical CQRS) that stores them; every module
/// declares the queries that may become widgets in one typed <c>IModuleWidgets</c> list, and nothing else is reachable
/// from a dashboard. A widget's query runs through the dispatcher as the calling user.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class DashboardsTests
{
    private const string Profile = "saas-standard-react";
    private const string Dashboards = "frontend.dashboards=user-editable";
    private const string Roles = "backend.authorization=roles";

    private const string RevenueWidget = """
        using Shop.BuildingBlocks.Dashboards;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Revenue;

        public sealed record Revenue : IQuery<Result<ChartSeries, ApiError>>;

        public sealed class RevenueHandler : IQueryHandler<Revenue, Result<ChartSeries, ApiError>>
        {
            public Task<Result<ChartSeries, ApiError>> HandleAsync(Revenue query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<ChartSeries, ApiError>>(Success(new ChartSeries(["January"], [new ChartValues("Revenue", [42m])])));
        }

        public sealed class BillingWidgets : IModuleWidgets
        {
            public void Declare(WidgetCatalogBuilder widgets) => widgets.Chart("revenue", "Revenue", () => new Revenue());
        }
        """;

    private const string RevenueWidgetWithoutHandler = """
        using Shop.BuildingBlocks.Dashboards;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Revenue;

        public sealed record Revenue : IQuery<Result<ChartSeries, ApiError>>;

        public sealed class BillingWidgets : IModuleWidgets
        {
            public void Declare(WidgetCatalogBuilder widgets) => widgets.Chart("revenue", "Revenue", () => new Revenue());
        }
        """;

    private const string RevenueWidgetInTheApi = """
        using Shop.BuildingBlocks.Dashboards;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Api.Revenue;

        public sealed record Revenue : IQuery<Result<ChartSeries, ApiError>>;

        public sealed class RevenueHandler : IQueryHandler<Revenue, Result<ChartSeries, ApiError>>
        {
            public Task<Result<ChartSeries, ApiError>> HandleAsync(Revenue query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<ChartSeries, ApiError>>(Success(new ChartSeries(["January"], [new ChartValues("Revenue", [42m])])));
        }

        public sealed class BillingWidgets : IModuleWidgets
        {
            public void Declare(WidgetCatalogBuilder widgets) => widgets.Chart("revenue", "Revenue", () => new Revenue());
        }
        """;

    private const string RevenueWidgetInTheBuildingBlocks = """
        namespace Shop.BuildingBlocks.Dashboards;

        public sealed class SharedWidgets : IModuleWidgets
        {
            public void Declare(WidgetCatalogBuilder widgets)
            {
            }
        }
        """;

    private const string ManagedRevenueWidget = """
        using Shop.BuildingBlocks.Dashboards;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Revenue;

        public sealed record Revenue : IQuery<Result<ChartSeries, ApiError>>;

        public sealed class RevenueHandler : IQueryHandler<Revenue, Result<ChartSeries, ApiError>>
        {
            public Task<Result<ChartSeries, ApiError>> HandleAsync(Revenue query, CancellationToken cancellationToken) =>
                Task.FromResult(Result<ChartSeries, ApiError>.Success(new ChartSeries(["January"], [new ChartValues("Revenue", [42m])])));
        }

        public sealed class BillingWidgets : IModuleWidgets
        {
            public void Declare(WidgetCatalogBuilder widgets) => widgets.Chart("revenue", "Revenue", () => new Revenue(), BillingPolicyNames.Manage);
        }
        """;

    private const string RevenueWidgetWithAnotherModulesPolicy = """
        using Shop.BuildingBlocks.Dashboards;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Revenue;

        public sealed record Revenue : IQuery<Result<ChartSeries, ApiError>>;

        public sealed class RevenueHandler : IQueryHandler<Revenue, Result<ChartSeries, ApiError>>
        {
            public Task<Result<ChartSeries, ApiError>> HandleAsync(Revenue query, CancellationToken cancellationToken) =>
                Task.FromResult(Result<ChartSeries, ApiError>.Success(new ChartSeries(["January"], [new ChartValues("Revenue", [42m])])));
        }

        public sealed class BillingWidgets : IModuleWidgets
        {
            public void Declare(WidgetCatalogBuilder widgets) => widgets.Chart("revenue", "Revenue", () => new Revenue(), "dashboards.manage");
        }
        """;

    /// <summary>The visitor holds no role: the billing widget that needs billing.manage is neither listed nor run for them.</summary>
    private const string ManagedWidgetTests = """
        using System.Net;
        using System.Net.Http.Json;
        using System.Text.Json.Nodes;

        namespace Shop.Integration.Tests;

        public class ManagedWidgetTests(AspireFixture aspire)
        {
            [Fact]
            public async Task A_widget_needing_a_policy_is_listed_and_run_only_for_a_caller_who_holds_it()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var manager = await aspire.CreateHostClientAsync("developer");
                using var visitor = await aspire.CreateHostClientAsync("visitor");

                var managersCatalog = await manager.GetFromJsonAsync<JsonObject>("/dashboards/widgets?page=1&pageSize=100", cancellationToken);
                var visitorsCatalog = await visitor.GetFromJsonAsync<JsonObject>("/dashboards/widgets?page=1&pageSize=100", cancellationToken);
                Ids(managersCatalog!).ShouldContain("billing.revenue");
                Ids(visitorsCatalog!).ShouldNotContain("billing.revenue");

                var run = await manager.GetFromJsonAsync<JsonObject>("/dashboards/widgets/billing.revenue?page=1&pageSize=20", cancellationToken);
                run!["chart"]!["series"]![0]!["values"]![0]!.GetValue<decimal>().ShouldBe(42m);
                (await visitor.GetAsync(new Uri("/dashboards/widgets/billing.revenue?page=1&pageSize=20", UriKind.Relative), cancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

                var declaration = await manager.GetFromJsonAsync<JsonObject>("/dashboards/widget-declarations/billing.revenue", cancellationToken);
                declaration!["title"]!.GetValue<string>().ShouldBe("Revenue");
                (await visitor.GetAsync(new Uri("/dashboards/widget-declarations/billing.revenue", UriKind.Relative), cancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }

            private static List<string> Ids(JsonObject catalog) =>
                [.. catalog["items"]!.AsArray().Select(widget => widget!["id"]!.GetValue<string>())];
        }
        """;

    [Fact]
    public void A_project_with_user_editable_dashboards_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Dashboards]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_pack_brings_a_dashboards_module_with_crud_and_logical_cqrs_and_declares_widgets_in_the_building_blocks()
    {
        var files = Files(Profile, Dashboards);

        files["fabric.yaml"].ShouldContain("  frontend.dashboards: [user-editable]\n");
        files["fabric.yaml"].ShouldContain("modules:\n  dashboards:\n    module.style: crud\n    module.cqrs: logical\n");
        files.Keys.ShouldContain("src/Modules/Dashboards/Dashboards.Domain/Dashboard.cs");
        files.Keys.ShouldContain("src/Modules/Dashboards/Dashboards.Api/DashboardsModule.cs");
        files.Keys.ShouldContain("tests/Modules/Dashboards.Tests/DashboardsModuleTests.cs");
        files["src/BuildingBlocks/Dashboards/IModuleWidgets.cs"].ShouldContain("public interface IModuleWidgets");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddDashboardWidgets(modules);");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/EveryWidgetIsADeclaredModuleQuery.cs");
        files.Keys.ShouldContain("tests/Integration.Tests/DashboardsTests.cs");
        files["Solution.slnx"].ShouldContain("src/Modules/Dashboards/Dashboards.Api/Dashboards.Api.csproj");
    }

    [Fact]
    public void The_frontends_openapi_document_describes_the_dashboards_endpoints()
    {
        Files(Profile, Dashboards)["web/app/openapi.json"].ShouldContain("\"/dashboards/{id}\"");
        Files("saas-standard-vue", Dashboards)["web/app/openapi.json"].ShouldContain("\"/dashboards/{id}\"");
    }

    [Fact]
    public void The_frontends_openapi_document_describes_the_lookup_of_one_widget_declaration()
    {
        Files(Profile, Dashboards)["web/app/openapi.json"].ShouldContain("\"/dashboards/widget-declarations/{id}\"");
        Files("saas-standard-vue", Dashboards)["web/app/openapi.json"].ShouldContain("\"/dashboards/widget-declarations/{id}\"");
    }

    [Fact]
    public void The_react_spa_gets_the_dashboard_page_on_react_grid_layout_and_the_module_page_renders_it()
    {
        var files = Files(Profile, Dashboards);

        files["web/app/package.json"].ShouldContain("\"react-grid-layout\": \"2.3.0\"");
        files["web/app/src/index.css"].ShouldStartWith("@import 'react-grid-layout/css/styles.css';\n");
        files["web/app/src/modules/dashboards/DashboardsPage.tsx"].ShouldContain("<DashboardEditor />");
        files.Keys.ShouldContain("web/app/src/dashboards/DashboardEditor.tsx");
        files.Keys.ShouldContain("web/app/src/dashboards/DashboardEditor.test.tsx");
        files.Keys.ShouldContain("web/app/e2e/dashboards.spec.ts");
    }

    [Fact]
    public void The_page_checks_accessibility_with_axe_only_where_the_project_has_the_accessibility_tests()
    {
        Files(Profile, Dashboards)["web/app/src/dashboards/DashboardEditor.test.tsx"].ShouldContain("from 'vitest-axe'");

        var without = Files(Profile, Dashboards, "frontend.test.extras=");

        without["web/app/package.json"].ShouldNotContain("vitest-axe");
        without["web/app/src/dashboards/DashboardEditor.test.tsx"].ShouldNotContain("axe");
    }

    /// <summary>
    /// Where the page imports no styles its <c>web.styles</c> contribution renders empty, and an empty contribution adds no
    /// separator: <c>src/index.css</c> stays as it is without dashboards, with no blank first line for Prettier to refuse.
    /// </summary>
    [Theory]
    [InlineData("lean-crud")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("saas-standard-vue")]
    public void Where_the_page_imports_no_styles_index_css_is_the_same_as_without_dashboards(string profile)
    {
        const string Styles = "web/app/src/index.css";

        Files(profile, Dashboards)[Styles].ShouldBe(Files(profile)[Styles]);
    }

    [Fact]
    public void Where_the_page_is_not_built_yet_the_module_keeps_the_routers_placeholder_page()
    {
        foreach (var (profile, overrides) in new[]
        {
            ("lean-crud", new[] { Dashboards }),
            ("industrial-event-driven-mfe", new[] { Dashboards }),
        })
        {
            var files = Files(profile, overrides);

            files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/dashboards/", StringComparison.Ordinal), profile);
            files.Single(file => file.Key.StartsWith("web/app/src/modules/dashboards/DashboardsPage.", StringComparison.Ordinal)).Value
                .ShouldNotContain("DashboardEditor", customMessage: profile);
        }
    }

    /// <summary>
    /// Only <c>spa</c> has a pack today, so no profile can reach another architecture; the two packs' templates are rendered
    /// with a project that chooses it, to show the module page names the editor exactly where the editor is generated.
    /// </summary>
    [Theory]
    [InlineData("spa")]
    [InlineData("multi-app")]
    public void The_module_page_renders_the_dashboard_editor_only_where_the_editor_is_generated(string architecture)
    {
        var router = Repository.Packs.Packs["frontend.routing.react-router"];
        var dashboards = Repository.Packs.Packs["frontend.dashboards.user-editable"];
        var choices = new ProjectChoices(
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["frontend.framework"] = ["react"],
                ["frontend.architecture"] = [architecture],
                ["frontend.api-client"] = ["rtk-query-codegen"],
                ["frontend.dashboards"] = ["user-editable"],
            },
            new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> { ["dashboards"] = new Dictionary<string, IReadOnlyList<string>>() });
        const string Page = "web/app/src/modules/dashboards/DashboardsPage.tsx";
        const string Editor = "web/app/src/dashboards/DashboardEditor.tsx";
        List<ComposedFile> files =
        [
            new(router, router.Files.Single(file => file.Template == "templates/module-page.tsx.sbn"), Page, "dashboards"),
            new(dashboards, dashboards.Files.Single(file => file.Target == Editor), Editor, null),
        ];

        var rendered = Renderer.Render(Repository.Catalog, new Composition([router, dashboards], files, new Dictionary<string, IReadOnlyList<SlotEntry>>(), []), choices, "shop");

        rendered.Errors.ShouldBeEmpty();
        var generated = rendered.Files.ToDictionary(file => file.Path, file => file.Content);
        generated[Page].Contains("DashboardEditor", StringComparison.Ordinal).ShouldBe(generated.ContainsKey(Editor), architecture);
    }

    [Fact]
    public void The_generated_web_app_builds_lints_and_passes_its_tests_and_the_end_to_end_test_finds_a_saved_dashboard_after_a_reload()
    {
        using var project = new GeneratedProject(Profile, Dashboards);
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("2 passed");
    }

    [Fact]
    public void The_realm_has_a_second_user_in_the_first_tenant_to_show_a_dashboard_is_its_owners_alone()
    {
        var realm = JsonNode.Parse(Files(Profile, Dashboards)["src/AppHost/realms/realm.json"])!;

        var users = realm["users"]!.AsArray().ToDictionary(user => (string)user!["username"]!);
        users.Keys.ShouldBe(["developer", "developer-b", "visitor"], ignoreOrder: true);
        users["visitor"]!["groups"]!.AsArray().Select(group => (string?)group).ShouldBe(["/tenants/tenant-a"]);
    }

    [Fact]
    public void Without_dashboards_no_profile_mentions_them()
    {
        foreach (var profile in new[] { "saas-standard-react", "saas-standard-vue", "lean-crud", "saas-strict-react" })
        {
            var files = Files(profile);

            files["fabric.yaml"].ShouldContain("  frontend.dashboards: []\n", customMessage: profile);
            files["fabric.yaml"].ShouldContain("modules: {}\n", customMessage: profile);
            files.Keys.ShouldNotContain(path => path.Contains("Dashboards", StringComparison.Ordinal), profile);
            files["src/AppHost/realms/realm.json"].ShouldNotContain("visitor", customMessage: profile);
        }
    }

    [Fact]
    public void Dashboards_need_authentication_and_generative_ones_need_user_editable_ones_and_agents()
    {
        Validate("backend.auth=none", Dashboards).ShouldContain("frontend.dashboards is set to user-editable, but it only applies when backend.auth != none, and backend.auth is none.");
        Validate("frontend.dashboards=generative").ShouldNotBeEmpty();
        Validate("frontend.dashboards=user-editable,generative").ShouldNotBeEmpty();
        Validate("backend.llm=anthropic", "backend.llm.agents=agent-framework", "frontend.dashboards=user-editable,generative").ShouldBeEmpty();
    }

    [Fact]
    public void The_untouched_project_builds_and_its_module_and_architecture_tests_pass_with_a_widget_in_another_module()
    {
        using var project = new GeneratedProject(Profile, Dashboards);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Revenue", RevenueWidget);
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Dashboards.Tests/Dashboards.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_widget_whose_query_has_no_handler_fails_the_architecture_tests_naming_it()
    {
        var output = ArchitectureFailure(Profile, "src/Modules/Billing/Billing.Application/Queries/Revenue", RevenueWidgetWithoutHandler, Dashboards);

        output.ShouldContain("The widget billing.revenue runs Shop.Modules.Billing.Application.Queries.Revenue.Revenue, which has 0 handlers in Shop.Modules.Billing.Application: a widget's query needs exactly one.");
    }

    [Fact]
    public void Widgets_declared_outside_the_modules_application_project_fail_the_architecture_tests_naming_them()
    {
        var output = ArchitectureFailure(Profile, "src/Modules/Billing/Billing.Api/Revenue", RevenueWidgetInTheApi, Dashboards);

        output.ShouldContain("Shop.Modules.Billing.Api.Revenue.BillingWidgets declares widgets in Shop.Modules.Billing.Api: a module declares its widgets, and their queries, in its Application project.");
    }

    [Fact]
    public void Widgets_declared_in_the_building_blocks_fail_the_architecture_tests_naming_them()
    {
        var output = ArchitectureFailure(Profile, "src/BuildingBlocks/Dashboards", RevenueWidgetInTheBuildingBlocks, Dashboards);

        output.ShouldContain("Shop.BuildingBlocks.Dashboards.SharedWidgets declares widgets in BuildingBlocks: a module declares its widgets, and their queries, in its Application project.");
    }

    [Fact]
    public void A_widget_naming_a_policy_its_module_does_not_register_fails_the_architecture_tests_naming_it()
    {
        var output = ArchitectureFailure("lean-crud", "src/Modules/Billing/Billing.Application/Queries/Revenue", RevenueWidgetWithAnotherModulesPolicy, Dashboards, Roles);

        output.ShouldContain("The widget billing.revenue requires the policy dashboards.manage, which the module Billing does not register.");
    }

    [Fact]
    public void With_minimal_results_bcl_guards_nullable_absence_and_roles_the_project_builds_and_its_tests_pass()
    {
        using var project = new GeneratedProject("lean-crud", Dashboards, Roles, "backend.absence=nullable", "backend.result-style=minimal", "backend.guards=bcl");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Revenue", ManagedRevenueWidget);
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Dashboards.Tests/Dashboards.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_integration_tests_save_and_reload_a_dashboard_that_another_user_and_another_tenant_cannot_see()
    {
        using var project = new GeneratedProject(Profile, Dashboards);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("skipped: 1");
    }

    [Fact]
    public void Without_tenancy_and_with_roles_the_integration_tests_pass_and_a_widget_needing_a_policy_reaches_only_its_holders()
    {
        using var project = new GeneratedProject("lean-crud", Dashboards, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Revenue", ManagedRevenueWidget.Replace(
            "Task.FromResult(Result<ChartSeries, ApiError>.Success(",
            "Task.FromResult<Result<ChartSeries, ApiError>>(Success(",
            StringComparison.Ordinal));
        project.Write("tests/Integration.Tests/ManagedWidgetTests.cs", ManagedWidgetTests);
        project.Build();

        // The billing module, added after generation, brings a sample endpoint the frontend's OpenAPI copy does not know
        // yet; that one test fails and rewrites the copy, as it does for any module added with roles.
        var (_, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        output.ShouldContain("failed: 1", customMessage: output);
        output.ShouldContain("failed Shop.Integration.Tests.OpenApiDocumentTests.The_frontends_copy_of_the_openapi_document_is_current", customMessage: output);
        output.ShouldNotContain("skipped: 1", customMessage: output);
    }

    private static string ArchitectureFailure(string profile, string directory, string source, params string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");
        project.WriteTypes(directory, source);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        // The host refuses to start on the same problems, so a rule that starts it would report them too; the widget
        // rule must report them itself.
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("failed Shop.Architecture.Tests.ArchitectureRulesTests.Rule_holds(rule: \"EveryWidgetIsADeclaredModuleQuery\")", customMessage: output);
        return output;
    }

    private static List<string> Validate(params string[] overrides)
    {
        var resolution = ChoiceResolver.Resolve(Repository.Catalog, Profile, overrides);
        return resolution.Choices is { } choices
            ? [.. RuleEvaluator.Check(Repository.Catalog, choices)]
            : [.. resolution.Errors];
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

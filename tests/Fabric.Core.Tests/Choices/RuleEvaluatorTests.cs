using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Tests.Choices;

public class RuleEvaluatorTests
{
    private static readonly CatalogModel RepositoryCatalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

    private static IReadOnlyList<string> Violations(string profile, params string[] overrides)
    {
        var resolution = ChoiceResolver.Resolve(RepositoryCatalog, profile, overrides);
        resolution.Errors.ShouldBeEmpty();
        return RuleEvaluator.Check(RepositoryCatalog, resolution.Choices.ShouldNotBeNull());
    }

    private static Dictionary<string, IReadOnlyList<string>> Values(params (string Dimension, string Option)[] values) =>
        values.ToDictionary(value => value.Dimension, value => (IReadOnlyList<string>)[value.Option]);

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("lean-crud")]
    [InlineData("saas-strict-react")]
    public void Every_profile_satisfies_every_rule(string profile)
    {
        Violations(profile).ShouldBeEmpty();
    }

    /// <summary>openapi-fetch is a plain typed client: it pairs with TanStack Query's caches, not with Redux Toolkit, whose server data is RTK Query (#264).</summary>
    [Theory]
    [InlineData("saas-standard-react", "frontend.state=tanstack-query")]
    [InlineData("lean-crud", "frontend.state=tanstack-query")]
    [InlineData("saas-standard-vue", "frontend.state=pinia-tanstack-query")]
    [InlineData("saas-standard-vue", "frontend.state=tanstack-query")]
    public void The_openapi_fetch_client_is_allowed_with_tanstack_query(string profile, string state)
    {
        Violations(profile, "frontend.api-client=openapi-fetch", state).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("industrial-event-driven-mfe")]
    public void Strict_ddd_depth_is_allowed_for_ddd_and_event_sourced_modules(string profile)
    {
        Violations(profile, "module.ddd-depth=strict").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("saas-standard-vue", "frontend.state=redux-toolkit",
        "frontend.state=redux-toolkit requires frontend.framework == react, but frontend.framework is vue.")]
    [InlineData("saas-standard-vue", "frontend.state=zustand-tanstack-query",
        "frontend.state=zustand-tanstack-query requires frontend.framework == react, but frontend.framework is vue.")]
    [InlineData("lean-crud", "frontend.state=pinia",
        "frontend.state=pinia requires frontend.framework == vue, but frontend.framework is react.")]
    [InlineData("saas-standard-react", "frontend.state=zustand-tanstack-query",
        "frontend.api-client=rtk-query-codegen requires frontend.state == redux-toolkit, but frontend.state is zustand-tanstack-query.")]
    [InlineData("saas-standard-react", "frontend.styling=css-modules",
        "frontend.ui-library=shadcn requires frontend.styling == tailwind, but frontend.styling is css-modules.")]
    [InlineData("saas-standard-react", "frontend.ui-library=mui",
        "frontend.ui-library=mui conflicts with frontend.styling == tailwind.")]
    [InlineData("saas-standard-react", "frontend.architecture=micro-frontends",
        "frontend.architecture=micro-frontends requires frontend.design-system != none, but frontend.design-system is none.")]
    [InlineData("industrial-event-driven-mfe", "frontend.mfe-mixed-frameworks=yes",
        "frontend.mfe-mixed-frameworks=yes requires frontend.design-system in (tokens, web-components), but frontend.design-system is tokens-and-components.")]
    [InlineData("saas-standard-react", "frontend.auth-client=msal",
        "frontend.auth-client=msal requires backend.auth == entra-id, but backend.auth is keycloak.")]
    [InlineData("saas-standard-react", "frontend.test.extras=storybook-tests",
        "frontend.test.extras=storybook-tests requires frontend.design-system == tokens-and-components, but frontend.design-system is none.")]
    [InlineData("lean-crud", "frontend.api-client=rtk-query-codegen",
        "frontend.api-client=rtk-query-codegen requires frontend.state == redux-toolkit, but frontend.state is tanstack-query.")]
    [InlineData("saas-standard-react", "frontend.api-client=openapi-fetch",
        "frontend.api-client=openapi-fetch requires frontend.state != redux-toolkit, but frontend.state is redux-toolkit.")]
    [InlineData("lean-crud", "backend.topology=services",
        "backend.topology=services requires backend.bus.transport != none, but backend.bus.transport is none.")]
    [InlineData("saas-standard-react", "backend.api-protocols=grpc",
        "backend.api-protocols=grpc requires backend.api-protocols has rest, but backend.api-protocols is grpc.")]
    [InlineData("lean-crud", "module.style=event-sourced",
        "module defaults: module.style=event-sourced requires module.cqrs != none, but module.cqrs is none.")]
    [InlineData("industrial-event-driven-mfe", "backend.database=sqlserver",
        "module defaults: module.event-store=marten requires backend.database == postgres, but backend.database is sqlserver.")]
    public void Rejects_a_known_invalid_combination_in_one_sentence(string profile, string setting, string violation)
    {
        Violations(profile, setting).ShouldContain(violation);
    }

    [Theory]
    [InlineData("industrial-event-driven-mfe", new[] { "backend.tenancy=column" },
        "module defaults: module.event-store=marten requires backend.tenancy == none, but backend.tenancy is column.")]
    [InlineData("industrial-event-driven-mfe", new[] { "backend.tenancy=schema-per-tenant" },
        "module defaults: module.event-store=marten requires backend.tenancy == none, but backend.tenancy is schema-per-tenant.")]
    [InlineData("industrial-event-driven-mfe", new[] { "backend.tenancy=database-per-tenant" },
        "module defaults: module.event-store=marten requires backend.tenancy == none, but backend.tenancy is database-per-tenant.")]
    [InlineData("saas-standard-react", new[] { "module.cqrs=physical" },
        "module defaults: module.cqrs=physical requires module.event-store == marten, but module.event-store is not set.")]
    [InlineData("industrial-event-driven-mfe", new[] { "module.event-store=kurrentdb" },
        "module defaults: module.cqrs=physical requires module.event-store == marten, but module.event-store is kurrentdb.")]
    [InlineData("industrial-event-driven-mfe", new[] { "module.event-store=kurrentdb", "module.cqrs=logical", "backend.tenancy=column" },
        "module defaults: module.event-store=kurrentdb requires backend.tenancy == none, but backend.tenancy is column.")]
    [InlineData("saas-standard-vue", new[] { "frontend.design-system=tokens-and-components" },
        "frontend.design-system=tokens-and-components requires frontend.framework == react, but frontend.framework is vue.")]
    [InlineData("saas-standard-vue", new[] { "frontend.architecture=micro-frontends", "frontend.design-system=tokens" },
        "frontend.mfe-technique=module-federation requires frontend.framework == react, but frontend.framework is vue.")]
    [InlineData("saas-standard-react", new[] { "frontend.styling=library-theme", "frontend.ui-library=radix-headless" },
        "frontend.styling=library-theme conflicts with frontend.ui-library in (shadcn, shadcn-vue, reka-ui, radix-headless).")]
    [InlineData("saas-standard-react", new[] { "backend.data-access.reads=dapper" },
        "backend.data-access.reads=dapper requires backend.tenancy == none, but backend.tenancy is column.")]
    [InlineData("lean-crud", new[] { "backend.database=sqlserver", "backend.tenancy=schema-per-tenant" },
        "backend.tenancy=schema-per-tenant requires backend.database == postgres, but backend.database is sqlserver.")]
    [InlineData("lean-crud", new[] { "module.communication=contract-interface" },
        "module defaults: module.communication requires module.communication has integration-events, but module.communication is contract-interface.")]
    public void Rejects_a_combination_the_packs_refuse_in_one_sentence(string profile, string[] settings, string violation)
    {
        Violations(profile, settings).ShouldBe([violation]);
    }

    [Theory]
    [InlineData("lean-crud", "backend.data-access.reads=dapper")]
    [InlineData("lean-crud", "backend.data-access.reads=dapper", "backend.database=sqlserver")]
    public void Dapper_reads_are_allowed_without_tenancy_on_either_database(string profile, params string[] settings)
    {
        Violations(profile, settings).ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_communication_set_is_refused_in_one_sentence()
    {
        Violations("lean-crud", "module.communication=")
            .ShouldBe(["module defaults: module.communication requires module.communication has integration-events, but module.communication is empty."]);
    }

    [Fact]
    public void A_module_dimension_requirement_is_checked_per_module_and_names_the_module()
    {
        var choices = ChoiceResolver.Resolve(RepositoryCatalog, "lean-crud", []).Choices.ShouldNotBeNull();
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules = new()
        {
            ["billing"] = new Dictionary<string, IReadOnlyList<string>> { ["module.communication"] = ["integration-events"] },
            ["shipping"] = new Dictionary<string, IReadOnlyList<string>> { ["module.communication"] = [] },
        };

        RuleEvaluator.Check(RepositoryCatalog, choices.Project, modules)
            .ShouldBe(["module 'shipping': module.communication requires module.communication has integration-events, but module.communication is empty."]);
    }

    [Fact]
    public void A_project_dimension_requirement_is_checked_whenever_the_dimension_is_set()
    {
        var catalog = CatalogReader.Read(
            new StringReader("""
                version: 1
                dimensions:
                  x.y: { scope: project, requires: ["x.z == p"], default: o, options: { o: { tier: supported } } }
                  x.z: { scope: project, default: q, options: { p: { tier: supported }, q: { tier: supported } } }
                """),
            "test.yaml");

        RuleEvaluator.Check(catalog, Values(("x.y", "o"), ("x.z", "q")), new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>())
            .ShouldBe(["x.y requires x.z == p, but x.z is q."]);
        RuleEvaluator.Check(catalog, Values(("x.z", "q")), new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>())
            .ShouldBeEmpty();
    }

    [Fact]
    public void Contract_interfaces_beside_integration_events_are_allowed()
    {
        Violations("lean-crud", "module.communication=integration-events,contract-interface").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("saas-standard-react", "shadcn")]
    [InlineData("saas-standard-vue", "shadcn-vue")]
    [InlineData("saas-standard-vue", "reka-ui")]
    public void Rejects_the_library_theme_of_a_tailwind_only_library(string profile, string library)
    {
        Violations(profile, "frontend.styling=library-theme", $"frontend.ui-library={library}")
            .ShouldContain("frontend.styling=library-theme conflicts with frontend.ui-library in (shadcn, shadcn-vue, reka-ui, radix-headless).");
    }

    [Theory]
    [InlineData("saas-standard-react", "mui")]
    [InlineData("saas-standard-vue", "vuetify")]
    public void A_library_with_its_own_theming_may_use_it(string profile, string library)
    {
        Violations(profile, "frontend.styling=library-theme", $"frontend.ui-library={library}").ShouldBeEmpty();
    }

    [Fact]
    public void Reka_ui_is_headless_so_it_may_be_styled_with_css_modules()
    {
        Violations("saas-standard-vue", "frontend.styling=css-modules", "frontend.ui-library=reka-ui").ShouldBeEmpty();
    }

    [Fact]
    public void Physical_cqrs_is_refused_on_each_module_that_is_not_event_sourced_on_marten()
    {
        var choices = ChoiceResolver.Resolve(RepositoryCatalog, "industrial-event-driven-mfe", []).Choices.ShouldNotBeNull();
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules = new()
        {
            ["ledger"] = Values(("module.style", "event-sourced"), ("module.cqrs", "physical"), ("module.event-store", "marten")),
            ["billing"] = Values(("module.style", "ddd"), ("module.cqrs", "physical")),
        };

        RuleEvaluator.Check(RepositoryCatalog, choices.Project, modules)
            .ShouldBe(["module 'billing': module.cqrs=physical requires module.event-store == marten, but module.event-store is not set."]);
    }

    [Fact]
    public void Rejects_rag_on_sql_server_in_one_sentence()
    {
        Violations("lean-crud", "backend.database=sqlserver", "backend.llm=openai", "backend.llm.extras=rag").ShouldBe(
            ["backend.llm.extras=rag requires backend.database == postgres, but backend.database is sqlserver."]);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    [InlineData("ollama")]
    public void Rag_on_postgres_satisfies_every_rule(string provider)
    {
        Violations("lean-crud", $"backend.llm={provider}", "backend.llm.extras=rag").ShouldBeEmpty();
    }

    [Fact]
    public void A_module_option_conflicting_with_a_project_option_is_reported()
    {
        Violations("saas-standard-react", "backend.topology=services", "module.communication=integration-events,contract-interface")
            .ShouldBe(["module defaults: module.communication=contract-interface conflicts with backend.topology == services."]);
    }

    [Fact]
    public void Module_rules_are_checked_per_module_and_name_the_module()
    {
        var choices = ChoiceResolver.Resolve(RepositoryCatalog, "saas-standard-react", ["backend.tenancy=none"]).Choices.ShouldNotBeNull();
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules = new()
        {
            ["billing"] = Values(("module.style", "ddd"), ("module.cqrs", "logical")),
            ["audit"] = Values(("module.style", "event-sourced"), ("module.cqrs", "none"), ("module.event-store", "marten")),
        };

        RuleEvaluator.Check(RepositoryCatalog, choices.Project, modules)
            .ShouldBe(["module 'audit': module.style=event-sourced requires module.cqrs != none, but module.cqrs is none."]);
    }

    [Fact]
    public void A_project_rule_on_a_module_dimension_holds_when_any_module_satisfies_it()
    {
        var catalog = CatalogReader.Read(
            new StringReader("""
                version: 1
                dimensions:
                  p.p:
                    scope: project
                    options:
                      audit-trail: { tier: supported, requires: ["m.m == event-sourced"] }
                  m.m:
                    scope: module
                    options: { crud: { tier: supported }, event-sourced: { tier: supported } }
                """),
            "test.yaml");
        var project = Values(("p.p", "audit-trail"));

        RuleEvaluator.Check(catalog, project, new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
        {
            ["a"] = Values(("m.m", "crud")),
            ["b"] = Values(("m.m", "event-sourced")),
        }).ShouldBeEmpty();

        RuleEvaluator.Check(catalog, project, new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
        {
            ["a"] = Values(("m.m", "crud")),
        }).ShouldBe(["p.p=audit-trail requires m.m == event-sourced, but no module satisfies it."]);
    }
}

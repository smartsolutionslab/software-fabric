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
    public void Every_profile_satisfies_every_rule(string profile)
    {
        Violations(profile).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("saas-standard-vue", "frontend.state=redux-toolkit",
        "frontend.state=redux-toolkit requires frontend.framework == react, but frontend.framework is vue.")]
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

    [Fact]
    public void A_module_option_conflicting_with_a_project_option_is_reported()
    {
        Violations("saas-standard-react", "backend.topology=services", "module.communication=contract-interface")
            .ShouldBe(["module defaults: module.communication=contract-interface conflicts with backend.topology == services."]);
    }

    [Fact]
    public void Module_rules_are_checked_per_module_and_name_the_module()
    {
        var choices = ChoiceResolver.Resolve(RepositoryCatalog, "saas-standard-react", []).Choices.ShouldNotBeNull();
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

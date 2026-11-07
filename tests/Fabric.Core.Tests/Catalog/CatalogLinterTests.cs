using Fabric.Core.Catalog;

namespace Fabric.Core.Tests.Catalog;

public class CatalogLinterTests
{
    private static IReadOnlyList<string> Lint(string dimensionsYaml, string profilesYaml = "{}") =>
        CatalogLinter.Check(CatalogReader.Read(
            new StringReader($"""
                version: 1
                profiles: {profilesYaml}
                dimensions:
                {dimensionsYaml}
                """),
            "test.yaml"));

    [Fact]
    public void The_repository_catalog_is_consistent()
    {
        CatalogLinter.Check(CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"))).ShouldBeEmpty();
    }

    [Fact]
    public void A_setting_default_outside_its_range_is_reported()
    {
        Lint("""
              x.y: { scope: project, options: { o: { tier: supported, settings: { threshold: { default: 6, minimum: 1, maximum: 5 } } } } }
            """).ShouldBe(["x.y=o: setting 'threshold' defaults to 6, outside its range 1 to 5."]);
    }

    [Fact]
    public void A_setting_minimum_above_its_maximum_is_reported()
    {
        Lint("""
              x.y: { scope: project, options: { o: { tier: supported, settings: { threshold: { default: 3, minimum: 5, maximum: 1 } } } } }
            """).ShouldBe(["x.y=o: setting 'threshold' has minimum 5 above its maximum 1."]);
    }

    [Fact]
    public void A_setting_on_a_module_scoped_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: module, default: o, options: { o: { tier: supported, settings: { threshold: { default: 3, minimum: 1, maximum: 5 } } } } }
            """).ShouldBe(["x.y=o: setting 'threshold' is on a module-scoped dimension; settings are project-wide, so only project-scoped options declare them."]);
    }

    [Theory]
    [InlineData("eval.threshold")]
    [InlineData("Threshold")]
    public void A_setting_id_that_is_not_lower_kebab_case_is_reported(string id)
    {
        Lint($$"""
              x.y: { scope: project, options: { o: { tier: supported, settings: { "{{id}}": { default: 3, minimum: 1, maximum: 5 } } } } }
            """).ShouldBe([$"x.y=o: setting '{id}' must be lower-case letters, digits and hyphens, starting with a letter."]);
    }

    [Fact]
    public void Default_from_naming_an_unknown_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: project, default-from: a.b, options: { o: { tier: supported } } }
            """).ShouldBe(["x.y: default-from names unknown dimension 'a.b'."]);
    }

    [Fact]
    public void Default_from_naming_its_own_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: project, default-from: x.y, options: { o: { tier: supported } } }
            """).ShouldBe(["x.y: default-from names its own dimension."]);
    }

    [Fact]
    public void Default_from_naming_a_multi_valued_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: project, default-from: x.z, options: { o: { tier: supported } } }
              x.z: { scope: project, multi: true, default: [o], options: { o: { tier: supported } } }
            """).ShouldBe(["x.y: default-from names multi-valued x.z; it can follow only a single-valued dimension."]);
    }

    [Fact]
    public void Default_from_on_a_multi_valued_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: project, multi: true, default-from: x.z, options: { o: { tier: supported } } }
              x.z: { scope: project, default: o, options: { o: { tier: supported } } }
            """).ShouldBe(["x.y: default-from is only for a single-valued dimension."]);
    }

    [Fact]
    public void Default_from_beside_a_default_is_reported()
    {
        Lint("""
              x.y: { scope: project, default: o, default-from: x.z, options: { o: { tier: supported } } }
              x.z: { scope: project, default: o, options: { o: { tier: supported } } }
            """).ShouldBe(["x.y: has both default and default-from; use one."]);
    }

    [Fact]
    public void Default_from_a_dimension_sharing_no_option_is_reported()
    {
        Lint("""
              x.y: { scope: project, default-from: x.z, options: { o: { tier: supported } } }
              x.z: { scope: project, default: p, options: { p: { tier: supported } } }
            """).ShouldBe(["x.y: default-from names x.z, which shares no option with it, so it never gives a default."]);
    }

    [Fact]
    public void Default_from_a_dimension_sharing_an_option_is_consistent()
    {
        Lint("""
              x.y: { scope: project, default-from: x.z, options: { o: { tier: supported } } }
              x.z: { scope: project, default: o, options: { o: { tier: supported }, p: { tier: supported } } }
            """).ShouldBeEmpty();
    }

    [Fact]
    public void A_condition_naming_an_unknown_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: project, default: o, options: { o: { tier: supported, requires: ["a.b == c"] } } }
            """).ShouldBe(["x.y=o: condition 'a.b == c' names unknown dimension 'a.b'."]);
    }

    [Fact]
    public void A_condition_naming_an_unknown_option_is_reported()
    {
        Lint("""
              x.y: { scope: project, default: o, options: { o: { tier: supported, conflicts: ["x.z in (p, q)"] } } }
              x.z: { scope: project, default: p, options: { p: { tier: supported } } }
            """).ShouldBe(["x.y=o: condition 'x.z in (p, q)' names 'q', which is not an option of x.z."]);
    }

    [Fact]
    public void Has_on_a_single_valued_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: project, default: o, options: { o: { tier: supported, requires: ["x.z has p"] } } }
              x.z: { scope: project, default: p, options: { p: { tier: supported } } }
            """).ShouldBe(["x.y=o: condition 'x.z has p' uses 'has' on single-valued x.z; use ==, != or in."]);
    }

    [Theory]
    [InlineData("x.z == p", "==")]
    [InlineData("x.z != p", "!=")]
    [InlineData("x.z in (p)", "in")]
    public void A_single_value_operator_on_a_multi_valued_dimension_is_reported(string condition, string op)
    {
        Lint($$"""
              x.y: { scope: project, default: o, options: { o: { tier: supported, requires: ["{{condition}}"] } } }
              x.z: { scope: project, multi: true, default: [p], options: { p: { tier: supported } } }
            """).ShouldBe([$"x.y=o: condition '{condition}' uses '{op}' on multi-valued x.z; use has."]);
    }

    [Fact]
    public void An_active_when_on_its_own_dimension_is_reported()
    {
        Lint("""
              x.y: { scope: project, active-when: "x.y == o", default: o, options: { o: { tier: supported } } }
            """).ShouldContain("x.y active-when: condition 'x.y == o' refers to its own dimension.");
    }

    [Fact]
    public void An_active_when_cycle_across_two_dimensions_is_reported_once_with_the_cycle_named()
    {
        Lint("""
              x.a: { scope: project, active-when: "x.b == on", default: on, options: { on: { tier: supported } } }
              x.b: { scope: project, active-when: "x.a == on", default: on, options: { on: { tier: supported } } }
            """).ShouldBe(["x.a active-when: cycle x.a -> x.b -> x.a; each dimension is active only if the next one is, so none can be decided."]);
    }

    [Fact]
    public void An_active_when_cycle_across_three_dimensions_is_reported_once_with_the_cycle_named()
    {
        Lint("""
              x.a: { scope: project, active-when: "x.b == on", default: on, options: { on: { tier: supported } } }
              x.b: { scope: project, active-when: "x.c == on", default: on, options: { on: { tier: supported } } }
              x.c: { scope: project, active-when: "x.a == on", default: on, options: { on: { tier: supported } } }
            """).ShouldBe(["x.a active-when: cycle x.a -> x.b -> x.c -> x.a; each dimension is active only if the next one is, so none can be decided."]);
    }

    [Fact]
    public void A_dimension_whose_active_when_leads_into_a_cycle_does_not_report_it_again()
    {
        Lint("""
              x.a: { scope: project, active-when: "x.b == on", default: on, options: { on: { tier: supported } } }
              x.b: { scope: project, active-when: "x.c == on", default: on, options: { on: { tier: supported } } }
              x.c: { scope: project, active-when: "x.b == on", default: on, options: { on: { tier: supported } } }
            """).ShouldBe(["x.b active-when: cycle x.b -> x.c -> x.b; each dimension is active only if the next one is, so none can be decided."]);
    }

    [Fact]
    public void An_active_when_chain_without_a_cycle_is_not_reported()
    {
        Lint("""
              x.a: { scope: project, active-when: "x.b == on", default: on, options: { on: { tier: supported } } }
              x.b: { scope: project, active-when: "x.c == on", default: on, options: { on: { tier: supported } } }
              x.c: { scope: project, default: on, options: { on: { tier: supported } } }
            """).ShouldBeEmpty();
    }

    [Fact]
    public void A_default_that_is_not_an_option_is_reported()
    {
        Lint("""
              x.y: { scope: project, default: z, options: { o: { tier: supported } } }
            """).ShouldBe(["x.y: default 'z' is not an option."]);
    }

    [Fact]
    public void A_single_valued_dimension_with_several_defaults_is_reported()
    {
        Lint("""
              x.y: { scope: project, default: [o, p], options: { o: { tier: supported }, p: { tier: supported } } }
            """).ShouldBe(["x.y: default has 2 values, but the dimension is single-valued."]);
    }

    [Fact]
    public void A_dimension_without_options_is_reported()
    {
        Lint("""
              x.y: { scope: project, options: {} }
            """).ShouldBe(["x.y: has no options."]);
    }

    [Fact]
    public void An_invalid_profile_is_reported_with_its_name()
    {
        Lint(
            """
              x.y: { scope: project, default: o, options: { o: { tier: supported }, p: { tier: supported, requires: ["x.z == q"] } } }
              x.z: { scope: project, default: r, options: { q: { tier: supported }, r: { tier: supported } } }
            """,
            "{ broken: { x.y: p } }")
            .ShouldBe(["profile 'broken': x.y=p requires x.z == q, but x.z is r."]);
    }

    [Fact]
    public void A_profile_that_does_not_resolve_is_reported_with_its_name()
    {
        Lint(
            """
              x.y: { scope: project, default: o, options: { o: { tier: supported } } }
            """,
            "{ broken: { x.y: nope } }")
            .ShouldBe(["profile 'broken': 'nope' is not an option of x.y; options: o."]);
    }
}

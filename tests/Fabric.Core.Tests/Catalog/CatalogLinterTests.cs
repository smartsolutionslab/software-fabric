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

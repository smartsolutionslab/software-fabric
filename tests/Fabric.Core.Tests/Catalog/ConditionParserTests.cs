using Fabric.Core.Catalog;

namespace Fabric.Core.Tests.Catalog;

public class ConditionParserTests
{
    [Theory]
    [InlineData("backend.database == postgres", ConditionOperator.Equals, new[] { "postgres" })]
    [InlineData("backend.bus.transport != none", ConditionOperator.NotEquals, new[] { "none" })]
    [InlineData("module.style in (ddd, event-sourced)", ConditionOperator.In, new[] { "ddd", "event-sourced" })]
    [InlineData("backend.api-protocols has rest", ConditionOperator.Has, new[] { "rest" })]
    public void Parses_each_operator(string text, ConditionOperator expectedOperator, string[] expectedOptions)
    {
        var condition = ConditionParser.Parse(text);

        condition.Operator.ShouldBe(expectedOperator);
        condition.Options.ShouldBe(expectedOptions);
    }

    [Fact]
    public void Reads_the_dimension_on_the_left()
    {
        ConditionParser.Parse("frontend.mfe-mixed-frameworks == yes").Dimension.ShouldBe("frontend.mfe-mixed-frameworks");
    }

    [Theory]
    [InlineData("a.b==c", "a.b == c")]
    [InlineData("  a.b   in(x,y ,  z)  ", "a.b in (x, y, z)")]
    [InlineData("a.b has c", "a.b has c")]
    public void Tolerates_whitespace_and_prints_canonically(string text, string canonical)
    {
        ConditionParser.Parse(text).ToString().ShouldBe(canonical);
    }

    [Theory]
    [InlineData("", 1, "a dimension")]
    [InlineData("a.b", 4, "an operator")]
    [InlineData("a.b = c", 5, "an operator")]
    [InlineData("a.b == ", 8, "an option")]
    [InlineData("a.b in x", 8, "'('")]
    [InlineData("a.b in (x, )", 12, "an option")]
    [InlineData("a.b in (x y)", 11, "',' or ')'")]
    [InlineData("a.b in (x", 10, "',' or ')'")]
    [InlineData("a.b == x y", 10, "the end")]
    [InlineData("a.b has (x)", 9, "an option")]
    [InlineData("A.b == c", 1, "a dimension")]
    public void Rejects_malformed_conditions_with_the_column(string text, int column, string expected)
    {
        var exception = Should.Throw<ConditionSyntaxException>(() => ConditionParser.Parse(text));

        exception.Column.ShouldBe(column);
        exception.Message.ShouldContain($"expected {expected}");
        exception.Message.ShouldContain($"column {column}");
    }

    [Fact]
    public void Every_condition_in_the_repository_catalog_parses()
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

        var conditions = catalog.Dimensions.Values
            .SelectMany(dimension => dimension.Options.Values
                .SelectMany(option => option.Requires.Concat(option.Conflicts))
                .Concat(dimension.ActiveWhen is { } activeWhen ? [activeWhen] : []))
            .ToList();

        conditions.Count.ShouldBe(40);
    }

    [Fact]
    public void The_reader_reports_a_malformed_condition_with_file_line_and_column()
    {
        var exception = Should.Throw<CatalogException>(() => CatalogReader.Read(
            new StringReader("""
                version: 1
                dimensions:
                  a.b:
                    scope: project
                    options:
                      x:
                        tier: supported
                        requires: ["c.d = e"]
                """),
            "test.yaml"));

        exception.Message.ShouldContain("test.yaml:8");
        exception.Message.ShouldContain("column 5");
        exception.Message.ShouldContain("'c.d = e'");
    }
}

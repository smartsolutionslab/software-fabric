using Fabric.Core.Catalog;
using Fabric.Core.Matrix;

namespace Fabric.Core.Tests.Matrix;

/// <summary>
/// A dimension's own <c>requires</c> binds whenever the dimension is set, as <see cref="Fabric.Core.Choices.RuleEvaluator"/>
/// checks it. The free dimensions between the rule's two ends hold more choice sets than the search's step limit, so the
/// search decides such a pair only if it checks the rule as soon as both ends are decided.
/// </summary>
public class DimensionRequiresTests
{
    private static readonly string CatalogText = $"""
        version: 1
        dimensions:
          backend.gate:
            scope: project
            default: open
            options:
              open: {"{"} tier: supported {"}"}
              shut: {"{"} tier: supported {"}"}
          backend.toggle:
            scope: project
            default: enabled
            options:
              enabled:  {"{"} tier: supported {"}"}
              disabled: {"{"} tier: supported {"}"}
        {string.Concat(Enumerable.Range(1, 7).Select(FreeDimension))}
          backend.reads:
            scope: project
            active-when: "backend.toggle == enabled"
            default: plain
            requires: ["backend.gate == open"]
            options:
              plain: {"{"} tier: supported {"}"}
              fast:  {"{"} tier: experimental {"}"}
        """;

    private static readonly CatalogModel Catalog = CatalogReader.Read(new StringReader(CatalogText), "catalog.yaml");

    private static readonly string[] Domain =
    [
        .. Catalog.Dimensions.Values.SelectMany(dimension => dimension.Options.Keys.Select(option => $"{dimension.Id}.{option}")),
    ];

    [Fact]
    public void A_pair_that_breaks_a_dimension_level_requires_is_infeasible_instead_of_exhausting_the_search()
    {
        var matrix = PairwiseMatrix.Build(Catalog, Domain, []);

        matrix.Errors.ShouldBeEmpty();
        matrix.InfeasiblePairs.Select(pair => string.Join(" & ", new[] { pair.First, pair.Second }.Order(StringComparer.Ordinal)))
            .ShouldBe(
                [
                    "backend.gate=shut & backend.reads=fast",
                    "backend.gate=shut & backend.reads=plain",
                    "backend.gate=shut & backend.toggle=enabled",
                    "backend.reads=fast & backend.toggle=disabled",
                    "backend.reads=plain & backend.toggle=disabled",
                ],
                ignoreOrder: true);
    }

    [Fact]
    public void A_dimension_level_requires_does_not_bind_while_its_dimension_is_inactive()
    {
        var matrix = PairwiseMatrix.Build(Catalog, Domain, []);

        matrix.Errors.ShouldBeEmpty();
        matrix.Combinations.ShouldContain(combination =>
            combination.Set.Contains("backend.gate=shut") && combination.Set.Contains("backend.toggle=disabled"));
    }

    private static string FreeDimension(int number) => $"""
          backend.free{number}:
            scope: project
            default: o1
            options:
        {string.Concat(Enumerable.Range(1, 8).Select(option => $"      o{option}: {{ tier: supported }}\n"))}
        """;
}

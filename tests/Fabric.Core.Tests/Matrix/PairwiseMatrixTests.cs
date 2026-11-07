using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Matrix;

namespace Fabric.Core.Tests.Matrix;

/// <summary>
/// The pairwise matrix (ADR-0003, M3-01), checked against every choice set of a catalog small enough to enumerate:
/// each pair some valid choice set contains is in a combination, and each other pair is listed as infeasible.
/// </summary>
public class PairwiseMatrixTests
{
    private const string CatalogText = """
        version: 1
        dimensions:
          backend.database:
            scope: project
            default: postgres
            options:
              postgres:  { tier: supported }
              sqlserver: { tier: supported }
              oracle:    { tier: experimental }
          backend.auth:
            scope: project
            default: keycloak
            options:
              keycloak: { tier: supported }
              none:     { tier: experimental }
          backend.authorization:
            scope: project
            multi: true
            active-when: backend.auth != none
            default: []
            options:
              roles:  { tier: experimental }
              claims: { tier: experimental, requires: ["backend.authorization has roles"] }
          frontend.framework:
            scope: project
            default: react
            options:
              react: { tier: supported }
              vue:   { tier: experimental }
          frontend.state:
            scope: project
            default: query
            options:
              redux: { tier: supported, requires: ["frontend.framework == react"] }
              pinia: { tier: experimental, requires: ["frontend.framework == vue"] }
              query: { tier: supported }
          module.style:
            scope: module
            default: ddd
            options:
              crud:          { tier: supported, conflicts: ["backend.database == sqlserver"] }
              ddd:           { tier: supported }
              event-sourced: { tier: supported }
          module.event-store:
            scope: module
            active-when: module.style == event-sourced
            default: marten
            options:
              marten: { tier: supported, requires: ["backend.database == postgres"] }
        """;

    private static readonly CatalogModel Catalog = CatalogReader.Read(new StringReader(CatalogText), "catalog.yaml");

    /// <summary>Every option has a pack except backend.database.oracle.</summary>
    private static readonly string[] Domain =
    [
        .. Catalog.Dimensions.Values
            .SelectMany(dimension => dimension.Options.Keys.Select(option => $"{dimension.Id}.{option}"))
            .Where(option => option != "backend.database.oracle"),
    ];

    private static MatrixResult Build(params MatrixExtra[] extras) => PairwiseMatrix.Build(Catalog, Domain, extras);

    [Fact]
    public void Every_pair_a_valid_choice_set_contains_is_in_some_combination()
    {
        var matrix = Build();

        matrix.Errors.ShouldBeEmpty();
        var covered = matrix.Combinations.SelectMany(combination => Pairs(Points(Resolve(combination.Set)))).ToHashSet();
        var feasible = FeasiblePairs();
        feasible.Except(covered).ShouldBeEmpty();
        matrix.FeasiblePairs.ShouldBe(feasible.Count);
    }

    [Fact]
    public void Every_pair_no_valid_choice_set_contains_is_listed_as_infeasible_and_no_other()
    {
        var matrix = Build();

        var listed = matrix.InfeasiblePairs.Select(pair => Pair(pair.First, pair.Second)).ToHashSet();
        var expected = AllPairs().Except(FeasiblePairs()).ToHashSet();
        listed.ShouldBe(expected, ignoreOrder: true);
        listed.ShouldContain(Pair("frontend.framework=vue", "frontend.state=redux"));
        listed.ShouldContain(Pair("backend.authorization has claims", "backend.authorization without roles"));
        listed.ShouldContain(Pair("backend.auth=none", "backend.authorization without roles"));
        listed.ShouldContain(Pair("backend.database=sqlserver", "module.style=event-sourced"));
    }

    [Fact]
    public void Every_combination_is_a_valid_choice_set_that_sets_each_active_dimension_project_and_module_alike()
    {
        var matrix = Build();

        matrix.Combinations.ShouldNotBeEmpty();
        foreach (var combination in matrix.Combinations)
        {
            var resolved = Resolve(combination.Set);
            combination.Set.Select(entry => entry[..entry.IndexOf('=', StringComparison.Ordinal)])
                .ShouldBe(resolved.Project.Keys.Concat(resolved.ModuleDefaults.Keys), $"{combination.Name} sets every active dimension, in catalog order");
        }

        matrix.Combinations.SelectMany(combination => combination.Set).ShouldContain("module.event-store=marten");
        matrix.Combinations.Select(combination => combination.Name).ShouldBe(
            [.. Enumerable.Range(1, matrix.Combinations.Count).Select(number => $"pairwise-{number}")]);
    }

    [Fact]
    public void An_option_without_a_pack_is_outside_the_matrix()
    {
        var matrix = Build();

        matrix.Combinations.SelectMany(combination => combination.Set).ShouldNotContain(entry => entry.Contains("oracle", StringComparison.Ordinal));
        matrix.InfeasiblePairs.ShouldNotContain(pair => pair.First.Contains("oracle", StringComparison.Ordinal) || pair.Second.Contains("oracle", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_catalog_and_packs_give_byte_identical_matrix_json_whatever_order_the_packs_come_in()
    {
        var first = MatrixFile.Write(PairwiseMatrix.Build(Catalog, Domain, []));
        var second = MatrixFile.Write(PairwiseMatrix.Build(Catalog, [.. Domain.Reverse()], []));

        second.ShouldBe(first);
    }

    [Fact]
    public void An_extra_combination_comes_first_with_its_reason_and_is_completed_to_a_valid_choice_set()
    {
        var extra = new MatrixExtra(
            "sqlserver with vue and roles broke once",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["backend.database"] = ["sqlserver"],
                ["frontend.framework"] = ["vue"],
                ["backend.authorization"] = ["roles"],
            });

        var matrix = Build(extra);

        matrix.Errors.ShouldBeEmpty();
        var first = matrix.Combinations[0];
        first.Name.ShouldBe("extra-1");
        first.Reason.ShouldBe("sqlserver with vue and roles broke once");
        first.Set.ShouldContain("backend.database=sqlserver");
        first.Set.ShouldContain("frontend.framework=vue");
        first.Set.ShouldContain("backend.authorization=roles");
        Resolve(first.Set);
        matrix.Combinations[1].Name.ShouldBe("pairwise-1");
        matrix.Combinations[1].Reason.ShouldBeNull();
    }

    [Fact]
    public void An_extra_no_valid_choice_set_contains_is_an_error_naming_its_reason()
    {
        var extra = new MatrixExtra(
            "event sourcing on sqlserver",
            new Dictionary<string, IReadOnlyList<string>> { ["backend.database"] = ["sqlserver"], ["module.style"] = ["event-sourced"] });

        var matrix = Build(extra);

        matrix.Errors.ShouldBe(["extra 'event sourcing on sqlserver': no valid choice set contains backend.database=sqlserver, module.style=event-sourced."]);
    }

    /// <summary>A dimension whose options have no pack has no factors; an extra pinning it empty still pins it active.</summary>
    [Fact]
    public void An_extra_pinning_a_dimension_without_packs_is_kept_active_or_is_an_error()
    {
        string[] domain = [.. Domain.Where(option => !option.StartsWith("backend.authorization.", StringComparison.Ordinal))];
        var empty = new Dictionary<string, IReadOnlyList<string>> { ["backend.authorization"] = [] };
        var impossible = new Dictionary<string, IReadOnlyList<string>> { ["backend.auth"] = ["none"], ["backend.authorization"] = [] };

        var kept = PairwiseMatrix.Build(Catalog, domain, [new MatrixExtra("no authorization", empty)]);
        var refused = PairwiseMatrix.Build(Catalog, domain, [new MatrixExtra("none", impossible)]);

        kept.Errors.ShouldBeEmpty();
        kept.Combinations[0].Set.ShouldContain("backend.authorization=");
        kept.Combinations[0].Set.ShouldContain("backend.auth=keycloak");
        refused.Errors.ShouldBe(["extra 'none': no valid choice set contains backend.auth=none, backend.authorization=."]);
    }

    /// <summary>
    /// Two dimensions without packs, each active only if the other is: the search would follow one to the other without
    /// end, so the matrix refuses the catalog with the linter's message before it searches.
    /// </summary>
    [Fact]
    public void An_active_when_cycle_is_an_error_naming_the_cycle_before_any_search()
    {
        var cyclic = CatalogReader.Read(
            new StringReader(CatalogText + """

                  x.a: { scope: project, active-when: "x.b == on", default: on, options: { on: { tier: supported } } }
                  x.b: { scope: project, active-when: "x.a == on", default: on, options: { on: { tier: supported } } }
                """),
            "catalog.yaml");

        var matrix = PairwiseMatrix.Build(cyclic, Domain, []);

        matrix.Errors.ShouldBe(["x.a active-when: cycle x.a -> x.b -> x.a; each dimension is active only if the next one is, so none can be decided."]);
        matrix.Combinations.ShouldBeEmpty();
    }

    /// <summary>Options with a pack in only one factor leave no pair to cover; an empty matrix would look like success.</summary>
    [Theory]
    [InlineData(
        "backend.database.postgres backend.database.sqlserver",
        "the options with a pack are the values of one factor (backend.database=postgres, backend.database=sqlserver), so there is no pair to cover; " +
        "the matrix needs options with a pack in two dimensions, or two options of a multi-valued one.")]
    [InlineData(
        "",
        "no option of the catalog has a pack, so there is no pair to cover; " +
        "the matrix needs options with a pack in two dimensions, or two options of a multi-valued one.")]
    public void A_domain_without_a_pair_to_cover_is_an_error_instead_of_an_empty_matrix(string domain, string error)
    {
        var matrix = PairwiseMatrix.Build(Catalog, domain.Split(' ', StringSplitOptions.RemoveEmptyEntries), []);

        matrix.Errors.ShouldBe([error]);
        matrix.Combinations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("backend.database", "oracle", "extra 'x': backend.database=oracle has no pack, so it is outside the matrix.")]
    [InlineData("backend.database", "db2", "extra 'x': 'db2' is not an option of backend.database.")]
    [InlineData("backend.queue", "rabbitmq", "extra 'x': unknown dimension 'backend.queue'.")]
    public void An_extra_naming_an_option_outside_the_matrix_is_an_error(string dimension, string option, string error)
    {
        var matrix = Build(new MatrixExtra("x", new Dictionary<string, IReadOnlyList<string>> { [dimension] = [option] }));

        matrix.Errors.ShouldBe([error]);
    }

    private static ResolvedChoices Resolve(IReadOnlyList<string> set)
    {
        var resolution = ChoiceResolver.Resolve(Catalog, null, set);
        resolution.Errors.ShouldBeEmpty(string.Join(" ", set));
        var choices = resolution.Choices.ShouldNotBeNull();
        RuleEvaluator.Check(Catalog, choices).ShouldBeEmpty(string.Join(" ", set));
        return choices;
    }

    /// <summary>The points of a resolved choice set: one per single-valued dimension, one per domain option of a multi-valued one.</summary>
    private static List<(string Factor, string Point)> Points(ResolvedChoices choices)
    {
        List<(string Factor, string Point)> points = [];
        foreach (var (dimensionId, chosen) in choices.Project.Concat(choices.ModuleDefaults))
        {
            var dimension = Catalog.Dimensions[dimensionId];
            if (!dimension.Multi)
            {
                points.Add((dimensionId, $"{dimensionId}={chosen[0]}"));
                continue;
            }

            foreach (var option in dimension.Options.Keys.Where(option => Domain.Contains($"{dimensionId}.{option}")))
            {
                points.Add(($"{dimensionId}#{option}", $"{dimensionId} {(chosen.Contains(option) ? "has" : "without")} {option}"));
            }
        }

        return points;
    }

    private static IEnumerable<(string, string)> Pairs(List<(string Factor, string Point)> points) =>
        from first in points
        from second in points
        where string.CompareOrdinal(first.Factor, second.Factor) < 0
        select Pair(first.Point, second.Point);

    private static (string, string) Pair(string first, string second) =>
        string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);

    /// <summary>Every value of every factor over the domain, whether or not a valid choice set reaches it.</summary>
    private static HashSet<(string, string)> AllPairs()
    {
        List<(string Factor, string Point)> points = [];
        foreach (var dimension in Catalog.Dimensions.Values)
        {
            foreach (var option in dimension.Options.Keys.Where(option => Domain.Contains($"{dimension.Id}.{option}")))
            {
                if (dimension.Multi)
                {
                    points.Add(($"{dimension.Id}#{option}", $"{dimension.Id} has {option}"));
                    points.Add(($"{dimension.Id}#{option}", $"{dimension.Id} without {option}"));
                }
                else
                {
                    points.Add((dimension.Id, $"{dimension.Id}={option}"));
                }
            }
        }

        return [.. Pairs(points)];
    }

    /// <summary>The pairs of every valid choice set over the domain, found by trying every value of every dimension.</summary>
    private static HashSet<(string, string)> FeasiblePairs()
    {
        IEnumerable<List<string>> sets = [[]];
        foreach (var dimension in Catalog.Dimensions.Values)
        {
            var options = dimension.Options.Keys.Where(option => Domain.Contains($"{dimension.Id}.{option}")).ToList();
            List<string?> values = dimension.Multi
                ? [.. Subsets(options).Select(subset => $"{dimension.Id}={string.Join(",", subset)}")]
                : [.. options.Select(option => $"{dimension.Id}={option}")];
            values.Add(null);
            sets = [.. sets.SelectMany(set => values.Select(value => value is null ? set : [.. set, value]))];
        }

        HashSet<(string, string)> pairs = [];
        foreach (var set in sets)
        {
            var resolution = ChoiceResolver.Resolve(Catalog, null, set);
            if (resolution.Choices is { } choices
                && RuleEvaluator.Check(Catalog, choices).Count == 0
                && choices.Project.Concat(choices.ModuleDefaults).All(entry => entry.Value.All(option => Domain.Contains($"{entry.Key}.{option}"))))
            {
                pairs.UnionWith(Pairs(Points(choices)));
            }
        }

        return pairs;
    }

    private static IEnumerable<List<string>> Subsets(List<string> options) =>
        Enumerable.Range(0, 1 << options.Count)
            .Select(mask => options.Where((_, index) => (mask & (1 << index)) != 0).ToList());
}

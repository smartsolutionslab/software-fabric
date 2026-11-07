using Fabric.Core.Catalog;

namespace Fabric.Core.Matrix;

/// <summary>
/// The pairwise matrix of ADR-0003: a small set of valid choice sets that together contain every pair of values of
/// two factors that any valid choice set contains. Built greedily, AETG-style, without randomness: each row is tried
/// from several pairs not yet covered, in catalog order, fills the other factors with the values that cover the most
/// open pairs, and the row covering the most is kept. A pair no valid choice set contains is listed, not dropped, so
/// a rule mistake shows in review. Validity is the catalog's own: <see cref="Choices.ChoiceResolver"/> and
/// <see cref="Choices.RuleEvaluator"/>.
/// </summary>
public static class PairwiseMatrix
{
    /// <summary>Rows tried per row kept: 40 gives 28 rows for today's packs where 1 gives 43, in about three seconds.</summary>
    private const int SeedsPerRow = 40;

    /// <param name="domain">The options with a pack, as <c>dimension.option</c>; any other entry is ignored.</param>
    /// <param name="extras">Combinations to put first, each completed like a pairwise row.</param>
    public static MatrixResult Build(CatalogModel catalog, IEnumerable<string> domain, IReadOnlyList<MatrixExtra> extras)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(extras);

        if (ActiveWhenCycles.Find(catalog) is { Count: > 0 } cycles)
        {
            return Failed(cycles);
        }

        var inDomain = domain.ToHashSet(StringComparer.Ordinal);
        var factors = Factors(catalog, inDomain);
        if (factors.Count < 2)
        {
            return Failed([NoPair(factors)]);
        }

        var firstPoint = new int[factors.Count];
        List<(int Factor, int Value)> points = [];
        for (var factor = 0; factor < factors.Count; factor++)
        {
            firstPoint[factor] = points.Count;
            points.AddRange(factors[factor].Labels.Select((_, value) => (factor, value)));
        }

        var uncovered = new bool[points.Count, points.Count];
        var candidatePairs = 0;
        for (var first = 0; first < points.Count; first++)
        {
            for (var second = first + 1; second < points.Count; second++)
            {
                if (points[first].Factor != points[second].Factor)
                {
                    uncovered[first, second] = true;
                    candidatePairs++;
                }
            }
        }

        var search = new MatrixSearch(catalog, factors);

        IEnumerable<int> Assigned() =>
            Enumerable.Range(0, factors.Count).Where(factor => search.Assignment[factor] >= 0).Select(factor => firstPoint[factor] + search.Assignment[factor]);

        bool Open(int point, int other) => uncovered[Math.Min(point, other), Math.Max(point, other)];

        // Open pairs with the values already chosen first; on a tie, open pairs with values still to be chosen, so the
        // rows at the end gather the last open pairs instead of taking one each.
        int Score(int factor, int value)
        {
            var point = firstPoint[factor] + value;
            var withChosen = Assigned().Count(other => other != point && Open(point, other));
            var withOpen = Enumerable.Range(0, points.Count)
                .Count(other => points[other].Factor != factor && search.Assignment[points[other].Factor] == MatrixSearch.Unassigned && Open(point, other));
            return (withChosen * points.Count) + withOpen;
        }

        IEnumerable<(int First, int Second)> PairsOf(List<int> assigned) =>
            from first in assigned
            from second in assigned
            where first < second
            select (first, second);

        int Gain(List<int> assigned) => PairsOf(assigned).Count(pair => uncovered[pair.First, pair.Second]);

        void Cover(List<int> assigned)
        {
            foreach (var (first, second) in PairsOf(assigned))
            {
                uncovered[first, second] = false;
            }
        }

        List<MatrixCombination> combinations = [];
        List<string> errors = [];
        foreach (var extra in extras)
        {
            var prefix = $"extra '{extra.Reason}': ";
            if (Seed(catalog, factors, inDomain, extra, prefix, errors) is not { } seed)
            {
                continue;
            }

            var pinnedDimensions = extra.Choices.Keys.Select(dimension => catalog.Dimensions.Keys.ToList().IndexOf(dimension));
            switch (search.Complete(seed, pinnedDimensions, Score))
            {
                case SearchOutcome.Found:
                    combinations.Add(new MatrixCombination($"extra-{combinations.Count + 1}", extra.Reason, search.Choices()));
                    Cover([.. Assigned()]);
                    break;
                case SearchOutcome.None:
                    errors.Add($"{prefix}no valid choice set contains {string.Join(", ", extra.Choices.Select(choice => $"{choice.Key}={string.Join(",", choice.Value)}"))}.");
                    break;
                default:
                    errors.Add($"{prefix}{OutOfSteps}");
                    break;
            }
        }

        List<(int First, int Second)> infeasible = [];
        var pairwise = 0;
        while (errors.Count == 0 && Seeds(uncovered, points.Count) is { Count: > 0 } seeds)
        {
            (IReadOnlyList<MatrixChoice> Choices, List<int> Points, int Gain)? best = null;
            foreach (var (first, second) in seeds)
            {
                var seed = new Dictionary<int, int> { [points[first].Factor] = points[first].Value, [points[second].Factor] = points[second].Value };
                switch (search.Complete(seed, [], Score))
                {
                    case SearchOutcome.Found:
                        var assigned = Assigned().ToList();
                        var gain = Gain(assigned);
                        if (best is null || gain > best.Value.Gain)
                        {
                            best = (search.Choices(), assigned, gain);
                        }

                        break;
                    case SearchOutcome.None:
                        infeasible.Add((first, second));
                        uncovered[first, second] = false;
                        break;
                    default:
                        errors.Add($"{Label(factors, points[first])} with {Label(factors, points[second])}: {OutOfSteps}");
                        uncovered[first, second] = false;
                        break;
                }
            }

            if (best is { } row)
            {
                combinations.Add(new MatrixCombination($"pairwise-{++pairwise}", null, row.Choices));
                Cover(row.Points);
            }
        }

        return errors.Count > 0
            ? Failed(errors)
            : new MatrixResult(
                combinations,
                candidatePairs - infeasible.Count,
                [.. infeasible.Order().Select(pair => new MatrixPair(Label(factors, points[pair.First]), Label(factors, points[pair.Second])))],
                []);
    }

    /// <summary>
    /// The pairs each new row is tried from: for each of the first <see cref="SeedsPerRow"/> points with an uncovered
    /// pair, its first one. The row that covers the most open pairs is kept, the earliest on a tie.
    /// </summary>
    private static List<(int First, int Second)> Seeds(bool[,] uncovered, int points)
    {
        List<(int First, int Second)> seeds = [];
        for (var first = 0; first < points && seeds.Count < SeedsPerRow; first++)
        {
            for (var second = first + 1; second < points; second++)
            {
                if (uncovered[first, second])
                {
                    seeds.Add((first, second));
                    break;
                }
            }
        }

        return seeds;
    }

    private static MatrixResult Failed(IReadOnlyList<string> errors) => new([], 0, [], errors);

    /// <summary>Pairs are of values of two factors, so one factor, or none, leaves the matrix nothing to cover.</summary>
    private static string NoPair(List<MatrixFactor> factors)
    {
        const string Needs = "so there is no pair to cover; the matrix needs options with a pack in two dimensions, or two options of a multi-valued one.";
        return factors.Count == 0
            ? $"no option of the catalog has a pack, {Needs}"
            : $"the options with a pack are the values of one factor ({string.Join(", ", factors[0].Labels)}), {Needs}";
    }

    private static string OutOfSteps =>
        $"the search tried {MatrixSearch.StepLimit} values without deciding whether a valid choice set contains these choices.";

    private static string Label(List<MatrixFactor> factors, (int Factor, int Value) point) => factors[point.Factor].Labels[point.Value];

    private static int IndexOf(IReadOnlyList<string> options, string option)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (options[index] == option)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Every dimension's factors over the domain, in catalog order.</summary>
    private static List<MatrixFactor> Factors(CatalogModel catalog, HashSet<string> domain)
    {
        List<MatrixFactor> factors = [];
        var dimensionIndex = 0;
        foreach (var dimension in catalog.Dimensions.Values)
        {
            var options = dimension.Options.Keys.Where(option => domain.Contains($"{dimension.Id}.{option}")).ToList();
            if (dimension.Multi)
            {
                factors.AddRange(options.Select(option => new MatrixFactor(
                    dimensionIndex,
                    dimension,
                    option,
                    [option],
                    [$"{dimension.Id} has {option}", $"{dimension.Id} without {option}"])));
            }
            else if (options.Count > 0)
            {
                factors.Add(new MatrixFactor(dimensionIndex, dimension, null, options, [.. options.Select(option => $"{dimension.Id}={option}")]));
            }

            dimensionIndex++;
        }

        return factors;
    }

    /// <summary>The extra's choices as factor values, or <c>null</c> after reporting why they are outside the matrix.</summary>
    private static Dictionary<int, int>? Seed(
        CatalogModel catalog,
        List<MatrixFactor> factors,
        HashSet<string> domain,
        MatrixExtra extra,
        string prefix,
        List<string> errors)
    {
        var before = errors.Count;
        Dictionary<int, int> seed = [];
        foreach (var (dimensionId, options) in extra.Choices)
        {
            if (!catalog.Dimensions.TryGetValue(dimensionId, out var dimension))
            {
                errors.Add($"{prefix}unknown dimension '{dimensionId}'.");
                continue;
            }

            if (!dimension.Multi && options.Count != 1)
            {
                errors.Add($"{prefix}{dimensionId} takes one option, got {options.Count}: {string.Join(", ", options)}.");
                continue;
            }

            foreach (var option in options)
            {
                if (!dimension.Options.ContainsKey(option))
                {
                    errors.Add($"{prefix}'{option}' is not an option of {dimensionId}.");
                }
                else if (!domain.Contains($"{dimensionId}.{option}"))
                {
                    errors.Add($"{prefix}{dimensionId}={option} has no pack, so it is outside the matrix.");
                }
            }

            for (var factor = 0; factor < factors.Count; factor++)
            {
                if (factors[factor].Dimension.Id != dimensionId)
                {
                    continue;
                }

                seed[factor] = factors[factor].Option is { } option
                    ? options.Contains(option) ? 0 : 1
                    : IndexOf(factors[factor].Options, options[0]);
            }
        }

        return errors.Count == before ? seed : null;
    }
}

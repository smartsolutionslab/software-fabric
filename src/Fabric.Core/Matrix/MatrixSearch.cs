using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Matrix;

/// <summary>
/// Extends a partial assignment of the factors to a valid choice set, or proves that none exists. A rule is checked
/// as soon as the dimension it names is decided, with <see cref="ConditionEvaluator"/>. After each step every open
/// factor must still have a value that breaks no decided rule (forward checking), and a factor left with a single
/// value is filled next; otherwise factors are filled in catalog order, each after the dimension its
/// <c>active-when</c> names. A complete assignment counts only once <see cref="ChoiceResolver"/> and
/// <see cref="RuleEvaluator"/> accept it, as <c>fabric validate</c> would.
/// </summary>
internal sealed class MatrixSearch
{
    public const int Unassigned = -1;
    public const int Inactive = -2;

    /// <summary>Values tried per search before it gives up; far above what the repository catalog needs.</summary>
    public const int StepLimit = 1_000_000;

    private static readonly Dictionary<string, IReadOnlyList<string>> Unset = [];

    private readonly CatalogModel catalog;
    private readonly IReadOnlyList<Dimension> dimensions;
    private readonly Dictionary<string, int> dimensionIndex;
    private readonly IReadOnlyList<MatrixFactor> factors;
    private readonly List<int>[] factorsOf;
    private readonly List<int>[] watchers;
    private readonly int[] order;
    private readonly int[] assignment;
    private int steps;

    public MatrixSearch(CatalogModel catalog, IReadOnlyList<MatrixFactor> factors)
    {
        this.catalog = catalog;
        this.factors = factors;
        dimensions = [.. catalog.Dimensions.Values];
        dimensionIndex = dimensions.Select((dimension, index) => (dimension.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index, StringComparer.Ordinal);
        factorsOf = [.. dimensions.Select(_ => new List<int>())];
        for (var factor = 0; factor < factors.Count; factor++)
        {
            factorsOf[factors[factor].DimensionIndex].Add(factor);
        }

        watchers = [.. dimensions.Select(_ => new List<int>())];
        for (var watcher = 0; watcher < dimensions.Count; watcher++)
        {
            var dimension = dimensions[watcher];
            IEnumerable<Condition> conditions = dimension.Options.Values.SelectMany(option => option.Requires.Concat(option.Conflicts));
            if (dimension.ActiveWhen is { } activeWhen)
            {
                conditions = conditions.Append(activeWhen);
            }

            var watched = conditions
                .Select(condition => dimensionIndex.TryGetValue(condition.Dimension, out var index) ? index : -1)
                .Append(watcher)
                .Where(index => index >= 0)
                .Distinct();
            foreach (var index in watched)
            {
                watchers[index].Add(watcher);
            }
        }

        order = [.. SearchOrder().SelectMany(dimension => factorsOf[dimension])];
        assignment = new int[factors.Count];
    }

    /// <summary>Each factor's value in the last assignment <see cref="Complete"/> found: a value index, or <see cref="Inactive"/>.</summary>
    public IReadOnlyList<int> Assignment => assignment;

    /// <summary>
    /// Fills every factor the seed leaves open, trying the values <paramref name="score"/> ranks highest first and
    /// breaking ties by catalog order, so the same input always gives the same choice set.
    /// </summary>
    public SearchOutcome Complete(IReadOnlyDictionary<int, int> seed, Func<int, int, int> score)
    {
        Array.Fill(assignment, Unassigned);
        foreach (var (factor, value) in seed)
        {
            assignment[factor] = value;
        }

        steps = 0;
        return Enumerable.Range(0, dimensions.Count).All(Check) ? Search(score) : SearchOutcome.None;
    }

    /// <summary>The found choice set: one choice per active dimension, in catalog order.</summary>
    public IReadOnlyList<MatrixChoice> Choices()
    {
        List<MatrixChoice> choices = [];
        for (var dimension = 0; dimension < dimensions.Count; dimension++)
        {
            if (State(dimension) is (true, { } values))
            {
                choices.Add(new MatrixChoice(dimensions[dimension].Id, dimensions[dimension].Multi, values));
            }
        }

        return choices;
    }

    private SearchOutcome Search(Func<int, int, int> score)
    {
        if (!Constrained(out var factor))
        {
            return SearchOutcome.None;
        }

        if (factor < 0)
        {
            return Valid() ? SearchOutcome.Found : SearchOutcome.None;
        }

        var dimension = factors[factor].DimensionIndex;
        var ranked = Enumerable.Range(0, factors[factor].Labels.Count).OrderByDescending(value => score(factor, value));
        IEnumerable<int> candidates = Active(dimension) switch
        {
            false => [Inactive],
            true => ranked,
            null => ranked.Append(Inactive),
        };

        foreach (var value in candidates)
        {
            if (++steps > StepLimit)
            {
                return SearchOutcome.OutOfSteps;
            }

            assignment[factor] = value;
            if (CheckAround(dimension))
            {
                var outcome = Search(score);
                if (outcome != SearchOutcome.None)
                {
                    return outcome;
                }
            }

            assignment[factor] = Unassigned;
        }

        return SearchOutcome.None;
    }

    /// <summary>
    /// Forward checking: every open factor must still have a value, or be inactive, without breaking a decided rule.
    /// A factor left with one such value is filled next, so what the seed forces is set at once and a contradiction
    /// shows before the search branches on unrelated dimensions; otherwise the first open factor in search order is.
    /// </summary>
    /// <param name="next">The factor to fill next, or -1 when every factor is filled.</param>
    private bool Constrained(out int next)
    {
        next = -1;
        var forced = false;
        foreach (var factor in order)
        {
            if (assignment[factor] != Unassigned)
            {
                continue;
            }

            var dimension = factors[factor].DimensionIndex;
            var possible = 0;
            for (var value = Inactive; value < factors[factor].Labels.Count; value++)
            {
                if (value == Unassigned)
                {
                    continue;
                }

                assignment[factor] = value;
                if (CheckAround(dimension))
                {
                    possible++;
                }
            }

            assignment[factor] = Unassigned;
            if (possible == 0)
            {
                return false;
            }

            if (next < 0 || (possible == 1 && !forced))
            {
                (next, forced) = (factor, possible == 1);
            }
        }

        return true;
    }

    private bool Valid()
    {
        var set = Choices().Select(choice => $"{choice.Dimension}={string.Join(",", choice.Options)}").ToList();
        var resolution = ChoiceResolver.Resolve(catalog, null, set);
        return resolution.Choices is { } choices && RuleEvaluator.Check(catalog, choices).Count == 0;
    }

    /// <summary>The checks a change to <paramref name="dimension"/> can affect: its own, and those of every dimension whose rules name it.</summary>
    private bool CheckAround(int dimension) => watchers[dimension].All(Check);

    /// <summary>The dimension's activity matches its factors, and no chosen option breaks a decided requires or conflicts rule.</summary>
    private bool Check(int dimension)
    {
        var own = factorsOf[dimension];
        var inactive = own.Count(factor => assignment[factor] == Inactive);
        var chosen = own.Count(factor => assignment[factor] >= 0);
        var active = Active(dimension);
        if ((inactive > 0 && chosen > 0)
            || (active == false && chosen > 0)
            || (active == true && (inactive > 0 || (own.Count == 0 && !dimensions[dimension].Multi))))
        {
            return false;
        }

        foreach (var option in Chosen(dimension))
        {
            if (option.Requires.Any(condition => Holds(condition) == false) || option.Conflicts.Any(condition => Holds(condition) == true))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<CatalogOption> Chosen(int dimension)
    {
        var options = dimensions[dimension].Options;
        foreach (var factor in factorsOf[dimension])
        {
            var value = assignment[factor];
            if (factors[factor].Option is { } option)
            {
                if (value == 0)
                {
                    yield return options[option];
                }
            }
            else if (value >= 0)
            {
                yield return options[factors[factor].Options[value]];
            }
        }
    }

    private bool? Active(int dimension) => dimensions[dimension].ActiveWhen is { } activeWhen ? Holds(activeWhen) : true;

    /// <summary>Whether the condition holds, or <c>null</c> while the dimension it names is undecided.</summary>
    private bool? Holds(Condition condition)
    {
        if (!dimensionIndex.TryGetValue(condition.Dimension, out var dimension))
        {
            return ConditionEvaluator.Holds(condition, Unset);
        }

        if (condition.Operator == ConditionOperator.Has && dimensions[dimension].Multi)
        {
            var index = factorsOf[dimension].FindIndex(factor => factors[factor].Option == condition.Options[0]);
            if (index < 0)
            {
                return false;
            }

            var value = assignment[factorsOf[dimension][index]];
            return value == Unassigned ? null : value == 0;
        }

        return State(dimension) switch
        {
            (false, _) => null,
            (true, null) => ConditionEvaluator.Holds(condition, Unset),
            (true, { } values) => ConditionEvaluator.Holds(condition, new Dictionary<string, IReadOnlyList<string>> { [condition.Dimension] = values }),
        };
    }

    /// <summary>Whether the dimension is decided, and if so its chosen options in catalog order, or <c>null</c> when it is inactive.</summary>
    private (bool Decided, IReadOnlyList<string>? Values) State(int dimension)
    {
        var own = factorsOf[dimension];
        if (own.Count == 0)
        {
            return Active(dimension) switch
            {
                null => (false, null),
                false => (true, null),
                true => (true, []),
            };
        }

        if (own.Any(factor => assignment[factor] == Unassigned))
        {
            return (false, null);
        }

        if (own.Any(factor => assignment[factor] == Inactive))
        {
            return (true, null);
        }

        return (true, factors[own[0]].Option is null
            ? [factors[own[0]].Options[assignment[own[0]]]]
            : [.. own.Where(factor => assignment[factor] == 0).Select(factor => factors[factor].Option!)]);
    }

    /// <summary>Catalog order, except that a dimension comes after the one its <c>active-when</c> names, so its activity is decided when it is filled.</summary>
    private List<int> SearchOrder()
    {
        List<int> placed = [];
        var remaining = Enumerable.Range(0, dimensions.Count).ToList();
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(
                dimension => dimensions[dimension].ActiveWhen is not { } activeWhen
                    || !dimensionIndex.TryGetValue(activeWhen.Dimension, out var named)
                    || placed.Contains(named),
                remaining[0]);
            placed.Add(next);
            remaining.Remove(next);
        }

        return placed;
    }
}

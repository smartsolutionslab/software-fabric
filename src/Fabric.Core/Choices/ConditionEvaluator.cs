using Fabric.Core.Catalog;

namespace Fabric.Core.Choices;

/// <summary>Evaluates a condition against chosen values. An unset dimension counts as inactive (docs/catalog-rules.md).</summary>
public static class ConditionEvaluator
{
    public static bool Holds(Condition condition, IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(values);

        if (!values.TryGetValue(condition.Dimension, out var chosen))
        {
            return condition.Operator == ConditionOperator.NotEquals;
        }

        return condition.Operator switch
        {
            ConditionOperator.Equals => chosen.SequenceEqual(condition.Options),
            ConditionOperator.NotEquals => !chosen.SequenceEqual(condition.Options),
            ConditionOperator.In => chosen.Count == 1 && condition.Options.Contains(chosen[0]),
            ConditionOperator.Has => chosen.Contains(condition.Options[0]),
            _ => throw new InvalidOperationException($"Unknown operator {condition.Operator}."),
        };
    }

    /// <summary>"backend.database is postgres", "backend.bus.library is not set" — for error messages.</summary>
    public static string DescribeActual(Condition condition, IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(values);

        return values.TryGetValue(condition.Dimension, out var chosen)
            ? $"{condition.Dimension} is {(chosen.Count == 0 ? "empty" : string.Join(", ", chosen))}"
            : $"{condition.Dimension} is not set";
    }
}

namespace Fabric.Core.Catalog;

public enum ConditionOperator
{
    Equals,
    NotEquals,
    In,
    Has,
}

/// <summary>One rule condition, as defined in docs/catalog-rules.md.</summary>
public sealed record Condition(string Dimension, ConditionOperator Operator, IReadOnlyList<string> Options)
{
    public override string ToString() => Operator switch
    {
        ConditionOperator.Equals => $"{Dimension} == {Options[0]}",
        ConditionOperator.NotEquals => $"{Dimension} != {Options[0]}",
        ConditionOperator.In => $"{Dimension} in ({string.Join(", ", Options)})",
        ConditionOperator.Has => $"{Dimension} has {Options[0]}",
        _ => throw new InvalidOperationException($"Unknown operator {Operator}."),
    };
}

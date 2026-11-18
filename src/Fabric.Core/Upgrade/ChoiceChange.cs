namespace Fabric.Core.Upgrade;

/// <summary>A choice <c>fabric change</c> sets: the dimension, what <c>fabric.yaml</c> records, and the new value, both as written in it.</summary>
public sealed record ChoiceChange(string Dimension, string From, string To);

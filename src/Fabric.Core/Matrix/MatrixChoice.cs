namespace Fabric.Core.Matrix;

/// <summary>One dimension's value in a matrix combination.</summary>
/// <param name="Multi">Whether the dimension is multi-valued, so <c>matrix.json</c> writes its options as a list even when there is one, or none.</param>
public sealed record MatrixChoice(string Dimension, bool Multi, IReadOnlyList<string> Options);

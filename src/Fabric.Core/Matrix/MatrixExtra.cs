namespace Fabric.Core.Matrix;

/// <summary>
/// A combination the matrix must contain besides its covering set (ADR-0003: a defect that needs three options
/// together is covered by adding that combination). Dimensions it leaves out are filled like a pairwise row's.
/// </summary>
/// <param name="Reason">Why the combination is in the matrix; shown next to it in <c>matrix.json</c>.</param>
/// <param name="Choices">The options it pins, by dimension; a multi-valued dimension's list is its whole value.</param>
public sealed record MatrixExtra(string Reason, IReadOnlyDictionary<string, IReadOnlyList<string>> Choices);

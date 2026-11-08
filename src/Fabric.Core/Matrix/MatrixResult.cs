namespace Fabric.Core.Matrix;

/// <summary>The covering set, how many pairs it covers, the pairs no valid choice set contains, or the errors.</summary>
public sealed record MatrixResult(
    IReadOnlyList<MatrixCombination> Combinations,
    int FeasiblePairs,
    IReadOnlyList<MatrixPair> InfeasiblePairs,
    IReadOnlyList<string> Errors);

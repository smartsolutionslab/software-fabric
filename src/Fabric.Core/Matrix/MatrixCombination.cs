namespace Fabric.Core.Matrix;

/// <summary>One row of the matrix: a full choice set over no profile.</summary>
/// <param name="Name"><c>extra-N</c> or <c>pairwise-N</c>, numbered from 1 in the order the rows were built.</param>
/// <param name="Reason">The extra's reason; <c>null</c> for a pairwise row.</param>
/// <param name="Choices">One per active dimension, project and module defaults, in catalog order.</param>
public sealed record MatrixCombination(string Name, string? Reason, IReadOnlyList<MatrixChoice> Choices)
{
    /// <summary>The choices as <c>--set dimension=option[,option]</c> arguments.</summary>
    public IReadOnlyList<string> Set => [.. Choices.Select(choice => $"{choice.Dimension}={string.Join(",", choice.Options)}")];
}

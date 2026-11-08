using Fabric.Core.Catalog;

namespace Fabric.Core.Matrix;

/// <summary>
/// One factor of the matrix. A single-valued dimension is one factor whose values are its options with a pack; a
/// multi-valued dimension is one factor per option with a pack, whose values are "has" (0) and "without" (1).
/// </summary>
/// <param name="DimensionIndex">The dimension's position in catalog order.</param>
/// <param name="Option">The option of a multi-valued dimension's factor; <c>null</c> for a single-valued dimension.</param>
/// <param name="Options">A single-valued dimension's options with a pack, in catalog order; the factor's values.</param>
/// <param name="Labels">Each value as a matrix pair names it: <c>dimension=option</c>, <c>dimension has option</c> or <c>dimension without option</c>.</param>
internal sealed record MatrixFactor(
    int DimensionIndex,
    Dimension Dimension,
    string? Option,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> Labels);

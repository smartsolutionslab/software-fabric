namespace Fabric.Core.Matrix;

/// <summary>
/// Two values of different factors: <c>dimension=option</c> for a single-valued dimension, and
/// <c>dimension has option</c> or <c>dimension without option</c> for one option of a multi-valued dimension.
/// </summary>
public sealed record MatrixPair(string First, string Second);

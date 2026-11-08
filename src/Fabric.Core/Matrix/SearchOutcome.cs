namespace Fabric.Core.Matrix;

internal enum SearchOutcome
{
    /// <summary>A valid choice set extends the seed.</summary>
    Found,

    /// <summary>No valid choice set extends the seed: every one was ruled out.</summary>
    None,

    /// <summary>The search stopped at its step limit before deciding.</summary>
    OutOfSteps,
}

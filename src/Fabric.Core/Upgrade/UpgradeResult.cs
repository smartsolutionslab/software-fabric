namespace Fabric.Core.Upgrade;

/// <summary>The summary of an upgrade that was applied, or the errors that stopped it before the project was touched.</summary>
public sealed record UpgradeResult(string? Summary, IReadOnlyList<string> Errors)
{
    /// <summary>The upgraded project's experimental options as <c>dimension=option</c> (ADR-0003); empty when it failed.</summary>
    public IReadOnlyList<string> Experimental { get; init; } = [];
}

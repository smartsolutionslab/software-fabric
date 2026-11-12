using Fabric.Core.Choices;

namespace Fabric.Core.Generation;

public sealed record FabricRecord(string Name, string? Profile, ProjectChoices Choices)
{
    /// <summary>The version of the generator that wrote the file last; <c>null</c> in a file from before it was recorded.</summary>
    public string? Generator { get; init; }

    /// <summary>The <c>sha256:</c> hash of the catalog that wrote the file last; <c>null</c> in a file from before it was recorded.</summary>
    public string? Catalog { get; init; }
}

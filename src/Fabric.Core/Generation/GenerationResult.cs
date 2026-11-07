using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

/// <summary>Either the files of a new project, ordered by path, or the errors that stopped it.</summary>
public sealed record GenerationResult(IReadOnlyList<RenderedFile> Files, IReadOnlyList<string> Errors)
{
    /// <summary>The project's experimental options as <c>dimension=option</c> (ADR-0003); empty when it failed.</summary>
    public IReadOnlyList<string> Experimental { get; init; } = [];
}

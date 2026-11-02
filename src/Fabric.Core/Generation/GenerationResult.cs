using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

/// <summary>Either the files of a new project, ordered by path, or the errors that stopped it.</summary>
public sealed record GenerationResult(IReadOnlyList<RenderedFile> Files, IReadOnlyList<string> Errors);

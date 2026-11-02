namespace Fabric.Core.Rendering;

public sealed record RenderedProject(IReadOnlyList<RenderedFile> Files, IReadOnlyList<string> Errors);

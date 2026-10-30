namespace Fabric.Core.Rendering;

/// <param name="Path">Target path relative to the project root, with forward slashes.</param>
public sealed record RenderedFile(string Path, string Content);

public sealed record RenderedProject(IReadOnlyList<RenderedFile> Files, IReadOnlyList<string> Errors);

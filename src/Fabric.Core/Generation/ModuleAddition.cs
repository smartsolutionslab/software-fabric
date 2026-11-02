using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

/// <summary>The files a new module creates and the updated <c>fabric.yaml</c>, or the errors that stopped it.</summary>
public sealed record ModuleAddition(IReadOnlyList<RenderedFile> Created, string? FabricYaml, IReadOnlyList<string> Errors);

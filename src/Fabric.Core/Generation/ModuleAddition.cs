using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

/// <summary>The files a new module creates and the updated <c>fabric.yaml</c>, or the errors that stopped it.</summary>
public sealed record ModuleAddition(IReadOnlyList<RenderedFile> Created, string? FabricYaml, IReadOnlyList<string> Errors)
{
    /// <summary>The experimental options the project has with the new module, as <c>dimension=option</c> (ADR-0003); empty when it failed.</summary>
    public IReadOnlyList<string> Experimental { get; init; } = [];

    /// <summary>
    /// The paths and schemas the module adds to the frontend's copy of the OpenAPI document (ADR-0005), or <c>null</c>
    /// when it adds none.
    /// </summary>
    public OpenApiAdditions? OpenApi { get; init; }
}

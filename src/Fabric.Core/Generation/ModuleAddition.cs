using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

/// <summary>
/// The files a new module creates and the updated <c>fabric.yaml</c>, or the errors that stopped it. A module that selects
/// packs new to the project creates nothing by itself: it comes with the whole project to merge (ADR-0005, amended
/// 2026-11-20).
/// </summary>
public sealed record ModuleAddition(IReadOnlyList<RenderedFile> Created, string? FabricYaml, IReadOnlyList<string> Errors)
{
    /// <summary>The experimental options the project has with the new module, as <c>dimension=option</c> (ADR-0003); empty when it failed.</summary>
    public IReadOnlyList<string> Experimental { get; init; } = [];

    /// <summary>
    /// The paths and schemas the module adds to the frontend's copy of the OpenAPI document (ADR-0005), or <c>null</c>
    /// when it adds none.
    /// </summary>
    public OpenApiAdditions? OpenApi { get; init; }

    /// <summary>The packs the module selects that the project has not, as <c>dimension=option</c>; empty when it has them all.</summary>
    public IReadOnlyList<string> NewPacks { get; init; } = [];

    /// <summary>
    /// The whole project as this fabric writes it with the module, <c>fabric.yaml</c> included, when <see cref="NewPacks"/>
    /// is not empty: those packs write into files the project has, so the module is merged into it as <c>fabric change</c>
    /// merges a choice (ADR-0016 C3). <c>null</c> when the module only creates files.
    /// </summary>
    public IReadOnlyList<RenderedFile>? Project { get; init; }
}

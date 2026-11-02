namespace Fabric.Core.Catalog;

/// <param name="Settings">Dimension id to selection, in file order. Single-valued dimensions hold one entry.</param>
public sealed record Profile(
    string Id,
    string? Extends,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Settings);

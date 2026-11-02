namespace Fabric.Core.Catalog;

/// <param name="Default">The default selection; <c>null</c> when the dimension has none. Single-valued dimensions hold one entry.</param>
/// <param name="RoleSlots">Slots every option's pack must declare, in file order (ADR-0008); other packs contribute to them whichever option is chosen.</param>
public sealed record Dimension(
    string Id,
    DimensionScope Scope,
    bool Multi,
    Condition? ActiveWhen,
    IReadOnlyList<string>? Default,
    string? Note,
    IReadOnlyDictionary<string, CatalogOption> Options,
    IReadOnlyDictionary<string, RoleSlot> RoleSlots);

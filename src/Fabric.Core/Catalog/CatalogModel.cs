namespace Fabric.Core.Catalog;

public sealed record CatalogModel(
    int Version,
    IReadOnlyDictionary<string, Dimension> Dimensions,
    IReadOnlyDictionary<string, Profile> Profiles);

public enum DimensionScope
{
    Project,
    Module,
}

public enum SupportTier
{
    Supported,
    Experimental,
}

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

/// <param name="Separator">Joins the slot's contributions; <c>null</c> means a newline. Every option's declaration must use it.</param>
public sealed record RoleSlot(string Slot, string Description, string? Separator);

public sealed record CatalogOption(
    string Id,
    SupportTier Tier,
    IReadOnlyList<Condition> Requires,
    IReadOnlyList<Condition> Conflicts,
    string? Note);

/// <param name="Settings">Dimension id to selection, in file order. Single-valued dimensions hold one entry.</param>
public sealed record Profile(
    string Id,
    string? Extends,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Settings);

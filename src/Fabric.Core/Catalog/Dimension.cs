namespace Fabric.Core.Catalog;

/// <param name="Default">The default selection; <c>null</c> when the dimension has none. Single-valued dimensions hold one entry.</param>
/// <param name="DefaultFrom">A single-valued dimension whose value is the default when it is one of this dimension's options; <c>null</c> when the default follows nothing.</param>
/// <param name="RoleSlots">Slots every option's pack must declare, in file order (ADR-0008); other packs contribute to them whichever option is chosen.</param>
public sealed record Dimension(
    string Id,
    DimensionScope Scope,
    bool Multi,
    Condition? ActiveWhen,
    IReadOnlyList<string>? Default,
    string? DefaultFrom,
    string? Note,
    IReadOnlyDictionary<string, CatalogOption> Options,
    IReadOnlyDictionary<string, RoleSlot> RoleSlots)
{
    /// <summary>Conditions that must all hold whenever the dimension is set, whichever options are chosen; checked per module for a module dimension.</summary>
    public IReadOnlyList<Condition> Requires { get; init; } = [];
}

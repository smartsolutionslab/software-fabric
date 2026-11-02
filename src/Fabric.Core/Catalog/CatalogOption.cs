namespace Fabric.Core.Catalog;

public sealed record CatalogOption(
    string Id,
    SupportTier Tier,
    IReadOnlyList<Condition> Requires,
    IReadOnlyList<Condition> Conflicts,
    string? Note);

namespace Fabric.Core.Catalog;

public sealed record CatalogOption(
    string Id,
    SupportTier Tier,
    IReadOnlyList<Condition> Requires,
    IReadOnlyList<Condition> Conflicts,
    string? Note)
{
    /// <summary>The numbers this option needs, in file order; empty for almost every option.</summary>
    public IReadOnlyDictionary<string, OptionSetting> Settings { get; init; } = new Dictionary<string, OptionSetting>();
}

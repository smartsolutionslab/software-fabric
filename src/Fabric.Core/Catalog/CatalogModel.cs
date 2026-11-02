namespace Fabric.Core.Catalog;

public sealed record CatalogModel(
    int Version,
    IReadOnlyDictionary<string, Dimension> Dimensions,
    IReadOnlyDictionary<string, Profile> Profiles);

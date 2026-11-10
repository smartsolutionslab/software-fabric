namespace Fabric.Core.Catalog;

/// <param name="Hash">SHA-256 of the catalog's text with LF line endings, as <c>sha256:&lt;hex&gt;</c>; recorded in <c>fabric.yaml</c>.</param>
public sealed record CatalogModel(
    int Version,
    IReadOnlyDictionary<string, Dimension> Dimensions,
    IReadOnlyDictionary<string, Profile> Profiles,
    string Hash);

using Fabric.Core.Catalog;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Generation;

/// <summary>The catalog and packs under tests/fixtures/minimal.</summary>
internal static class Fixtures
{
    public static string MinimalRoot { get; } = RepositoryRoot.File("tests", "fixtures", "minimal");

    public static CatalogModel MinimalCatalog { get; } = CatalogReader.ReadFile(Path.Combine(MinimalRoot, "catalog.yaml"));

    public static IReadOnlyDictionary<string, PackManifest> MinimalPacks { get; } = LoadPacks();

    private static IReadOnlyDictionary<string, PackManifest> LoadPacks()
    {
        var result = PackLoader.Load(Path.Combine(MinimalRoot, "packs"), MinimalCatalog);
        result.Errors.ShouldBeEmpty();
        return result.Packs;
    }
}

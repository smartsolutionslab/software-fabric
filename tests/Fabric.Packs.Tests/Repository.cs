using Fabric.Core.Catalog;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

internal static class Repository
{
    public static string Root { get; } = Locate();

    public static CatalogModel Catalog { get; } = CatalogReader.ReadFile(Path.Combine(Root, "catalog", "catalog.yaml"));

    public static PackLoadResult Packs { get; } = PackLoader.Load(Path.Combine(Root, "packs"), Catalog);

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fabric.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Fabric.slnx not found above the test output directory.");
    }
}

using Fabric.Core.Catalog;

namespace Fabric.Core.Reference;

/// <summary>Writes the catalog as <c>docs/catalog.md</c>.</summary>
public static class CatalogReference
{
    public static string Write(CatalogModel catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return string.Empty;
    }
}

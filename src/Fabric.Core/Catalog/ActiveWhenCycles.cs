namespace Fabric.Core.Catalog;

/// <summary>
/// Dimensions each active only if the next one is, around to the first: none can be decided. The linter reports them,
/// and the matrix refuses a catalog with one before it searches, since its search would follow them without end.
/// </summary>
public static class ActiveWhenCycles
{
    /// <summary>
    /// Each dimension has at most one active-when, so following them from a dimension either ends or returns to a dimension
    /// already passed. A cycle is reported once, from its member that comes first in the catalog; a self-reference is left to
    /// the linter's own check.
    /// </summary>
    public static IReadOnlyList<string> Find(CatalogModel catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        List<string> cycles = [];
        HashSet<string> reported = new(StringComparer.Ordinal);
        foreach (var start in catalog.Dimensions.Keys)
        {
            if (reported.Contains(start))
            {
                continue;
            }

            List<string> path = [start];
            var current = start;
            while (catalog.Dimensions.TryGetValue(current, out var dimension)
                && dimension.ActiveWhen is { } activeWhen
                && activeWhen.Dimension != current
                && !path.Contains(activeWhen.Dimension, StringComparer.Ordinal))
            {
                current = activeWhen.Dimension;
                path.Add(current);
            }

            var closes = catalog.Dimensions.TryGetValue(current, out var last) && last.ActiveWhen?.Dimension == start;
            if (path.Count > 1 && closes)
            {
                reported.UnionWith(path);
                cycles.Add($"{start} active-when: cycle {string.Join(" -> ", path)} -> {start}; " +
                    "each dimension is active only if the next one is, so none can be decided.");
            }
        }

        return cycles;
    }
}

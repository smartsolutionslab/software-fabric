using Fabric.Core.Catalog;

namespace Fabric.Core.Choices;

/// <summary>The chosen options whose tier is <c>experimental</c>, which are generated with a warning (ADR-0003).</summary>
public static class ExperimentalOptions
{
    /// <summary>
    /// Each experimental option chosen anywhere in the project, its module defaults or its modules, once, as
    /// <c>dimension=option</c>, in catalog order.
    /// </summary>
    public static IReadOnlyList<string> Chosen(CatalogModel catalog, ProjectChoices choices)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(choices);

        IEnumerable<IReadOnlyDictionary<string, IReadOnlyList<string>>> scopes = [choices.Project, choices.ModuleDefaults, .. choices.Modules.Values];
        List<string> chosen = [];
        foreach (var (dimensionId, dimension) in catalog.Dimensions)
        {
            var values = scopes
                .SelectMany(scope => scope.TryGetValue(dimensionId, out var options) ? options : [])
                .ToHashSet(StringComparer.Ordinal);
            chosen.AddRange(dimension.Options.Values
                .Where(option => option.Tier == SupportTier.Experimental && values.Contains(option.Id))
                .Select(option => $"{dimensionId}={option.Id}"));
        }

        return chosen;
    }
}

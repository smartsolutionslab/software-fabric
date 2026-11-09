using System.Text.Json;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Tests.Catalog;

/// <summary>The two layers that build an option (ADR-0003): the catalog's profiles and the combinations of <c>build/matrix.json</c>.</summary>
internal static class SupportTierCheck
{
    /// <summary>
    /// Each <c>supported</c> option that no resolved profile (its project values and module defaults) and no matrix
    /// combination chooses, as <c>dimension=option</c>, in catalog order.
    /// </summary>
    public static IReadOnlyList<string> Unbuilt(CatalogModel catalog, string matrixJson)
    {
        HashSet<string> built = [.. ProfileOptions(catalog), .. MatrixOptions(matrixJson)];

        return [.. catalog.Dimensions.Values.SelectMany(dimension => dimension.Options.Values
            .Where(option => option.Tier == SupportTier.Supported && !built.Contains($"{dimension.Id}={option.Id}"))
            .Select(option => $"{dimension.Id}={option.Id}"))];
    }

    private static IEnumerable<string> ProfileOptions(CatalogModel catalog)
    {
        foreach (var profile in catalog.Profiles.Keys)
        {
            var resolution = ChoiceResolver.Resolve(catalog, profile, []);
            var choices = resolution.Choices
                ?? throw new InvalidOperationException($"profile {profile} does not resolve: {string.Join(" ", resolution.Errors)}");
            foreach (var (dimension, options) in choices.Project.Concat(choices.ModuleDefaults))
            {
                foreach (var option in options)
                {
                    yield return $"{dimension}={option}";
                }
            }
        }
    }

    private static List<string> MatrixOptions(string matrixJson)
    {
        using var document = JsonDocument.Parse(matrixJson);
        List<string> options = [];
        foreach (var combination in document.RootElement.GetProperty("combinations").EnumerateArray())
        {
            foreach (var choice in combination.GetProperty("choices").EnumerateObject())
            {
                options.AddRange(choice.Value.ValueKind == JsonValueKind.Array
                    ? choice.Value.EnumerateArray().Select(option => $"{choice.Name}={option.GetString()}")
                    : [$"{choice.Name}={choice.Value.GetString()}"]);
            }
        }

        return options;
    }
}

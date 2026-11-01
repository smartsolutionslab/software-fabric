using Fabric.Core.Catalog;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Catalog;

/// <summary>ADR-0003: nothing is supported that the matrix does not build, and an option without a pack builds nothing.</summary>
public class SupportTierTests
{
    [Fact]
    public void Every_supported_option_of_the_repository_catalog_has_a_pack()
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));
        var packs = PackLoader.Load(RepositoryRoot.File("packs"), catalog).Packs;

        var supportedWithoutPack = catalog.Dimensions.Values
            .SelectMany(dimension => dimension.Options.Values
                .Where(option => option.Tier == SupportTier.Supported && !packs.ContainsKey($"{dimension.Id}.{option.Id}"))
                .Select(option => $"{dimension.Id}={option.Id}"))
            .ToList();

        supportedWithoutPack.ShouldBeEmpty(
            $"these options are tier: supported but have no pack under packs/; mark them experimental until their pack lands: {string.Join(", ", supportedWithoutPack)}");
    }
}

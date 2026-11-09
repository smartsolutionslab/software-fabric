using Fabric.Core.Catalog;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Catalog;

/// <summary>
/// ADR-0003: nothing is supported that the matrix does not build. An option without a pack builds nothing, and a
/// <c>supported</c> option is in a profile or in <c>build/matrix.json</c>, the two layers that build it.
/// </summary>
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

    [Fact]
    public void Every_supported_option_of_the_repository_catalog_is_in_a_profile_or_the_matrix()
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));
        var matrix = File.ReadAllText(RepositoryRoot.File("build", "matrix.json"));

        var unbuilt = SupportTierCheck.Unbuilt(catalog, matrix);

        unbuilt.ShouldBeEmpty(
            $"these options are tier: supported but in no profile and no combination of build/matrix.json, so nothing builds them; " +
            $"regenerate the matrix with fabric matrix, or mark them experimental: {string.Join(", ", unbuilt)}");
    }

    [Fact]
    public void A_supported_option_in_neither_a_profile_nor_the_matrix_is_named_and_nothing_else_is()
    {
        // x.a=q only in a profile, x.a=p and x.b=p by default in that profile; x.b=q and x.m=two only in the matrix,
        // x.b=q as one value of a multi-valued choice. x.b=s is supported and in neither; x.b=r is experimental.
        var catalog = CatalogReader.Read(
            new StringReader("""
                version: 1
                profiles:
                  only-profile:
                    x.a: q
                dimensions:
                  x.a:
                    scope: project
                    default: p
                    options:
                      p: { tier: supported }
                      q: { tier: supported }
                  x.b:
                    scope: project
                    multi: true
                    default: [p]
                    options:
                      p: { tier: supported }
                      q: { tier: supported }
                      r: { tier: experimental }
                      s: { tier: supported }
                  x.m:
                    scope: module
                    default: one
                    options:
                      one: { tier: supported }
                      two: { tier: supported }
                """),
            "inline");
        const string Matrix = """
            {
              "combinations": [
                { "name": "pairwise-1", "choices": { "x.a": "p", "x.b": ["p", "q"], "x.m": "two" } }
              ],
              "infeasiblePairs": []
            }
            """;

        SupportTierCheck.Unbuilt(catalog, Matrix).ShouldBe(["x.b=s"]);
    }
}

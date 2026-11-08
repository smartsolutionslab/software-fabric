using Fabric.Core.Catalog;
using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.data-access.reads: ef-core</c>, the default of a dimension active in every profile (owner decisions on
/// #247). Its pack adds four things to every profile and nothing else: the <c>fabric.yaml</c> line, one last ADR, one
/// AGENTS.md section and the gate <c>NoDapper</c>, which fails a module that references Dapper.
/// </summary>
public sealed class EfCoreReadsTests
{
    private const string Reads = "backend.data-access.reads";

    private const string ApplicationUsingDapper = """
        using Dapper;

        namespace Shop.Modules.Billing.Application;

        public static class InvoiceCount
        {
            public static async Task<int> CountAsync(System.Data.Common.DbConnection connection) =>
                await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM billing.invoices");
        }
        """;

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_profile_differs_from_the_same_profile_without_reads_by_exactly_four_additions(string profile)
    {
        var with = Files(Repository.Catalog, Repository.Packs.Packs, profile);
        var without = Files(
            Repository.Catalog with { Dimensions = new OrderedDictionary<string, Dimension>(Repository.Catalog.Dimensions.Where(dimension => dimension.Key != Reads)) },
            Repository.Packs.Packs.Where(pack => !pack.Key.StartsWith(Reads + ".", StringComparison.Ordinal)).ToDictionary(),
            profile);

        var adrs = without.Keys.Count(path => path.StartsWith("docs/adr/", StringComparison.Ordinal));
        without.Keys.Except(with.Keys).ShouldBeEmpty(profile);
        with.Keys.Except(without.Keys).Order(StringComparer.Ordinal).ToList().ShouldBe(
            [$"docs/adr/{adrs + 1:0000}-backend-data-access-reads-ef-core.md", "tests/Architecture.Tests/Rules/NoDapper.cs"],
            customMessage: profile);
        with.Keys.Where(path => without.ContainsKey(path) && with[path] != without[path]).Order(StringComparer.Ordinal).ToList().ShouldBe(
            ["AGENTS.md", "fabric.yaml"],
            customMessage: profile);

        Inserted(without["fabric.yaml"], with["fabric.yaml"]).ShouldBe([$"  {Reads}: ef-core"], customMessage: profile);
        string.Join('\n', Inserted(without["AGENTS.md"], with["AGENTS.md"])).Trim().ShouldBe(
            File.ReadAllText(Path.Combine(Repository.Root, "packs/backend/data-access/reads/ef-core/docs/agents.md")).ReplaceLineEndings("\n").Trim(),
            profile);
    }

    [Fact]
    public void A_module_reading_with_dapper_fails_the_architecture_tests_naming_the_module()
    {
        using var project = new GeneratedProject("lean-crud");
        project.AddModule("billing");
        var application = Path.Combine(project.ProjectDirectory, "src/Modules/Billing/Billing.Application/Billing.Application.csproj");
        File.WriteAllText(application, File.ReadAllText(application).Replace(
            "</Project>",
            "  <ItemGroup>\n    <PackageReference Include=\"Dapper\" VersionOverride=\"2.1.89\" />\n  </ItemGroup>\n\n</Project>",
            StringComparison.Ordinal));
        project.WriteTypes("src/Modules/Billing/Billing.Application", ApplicationUsingDapper);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application references Dapper, but this project reads through EF Core (backend.data-access.reads: ef-core).");
    }

    private static Dictionary<string, string> Files(CatalogModel catalog, IReadOnlyDictionary<string, PackManifest> packs, string profile)
    {
        var generated = Generator.New(catalog, packs, "shop", profile, []);
        generated.Errors.ShouldBeEmpty(profile);
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>The lines <paramref name="after"/> has between the lines it shares with <paramref name="before"/> at its start and its end, when <paramref name="before"/> has nothing there.</summary>
    private static string[] Inserted(string before, string after)
    {
        var old = before.Split('\n');
        var now = after.Split('\n');
        var start = 0;
        while (start < old.Length && start < now.Length && old[start] == now[start])
        {
            start++;
        }

        var end = 0;
        while (end < old.Length - start && end < now.Length - start && old[^(end + 1)] == now[^(end + 1)])
        {
            end++;
        }

        old[start..^end].ShouldBeEmpty("a line was changed or removed, not only added");
        return now[start..^end];
    }
}

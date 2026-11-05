using System.Text.RegularExpressions;
using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>Solution.slnx</c> lists the projects present at generation (ADR-0005 of the base), the modules a pack brings
/// included, so IDE and solution-wide test runs see them.
/// </summary>
public sealed partial class SolutionTests
{
    public static TheoryData<string> Profiles() => [.. Repository.Catalog.Profiles.Keys];

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_solution_lists_every_generated_project_and_nothing_else_including_a_brought_module(string profile)
    {
        var packs = Repository.Packs.Packs.ToDictionary();
        packs["backend.database.postgres"] = packs["backend.database.postgres"] with
        {
            Modules = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
            {
                ["reports"] = new Dictionary<string, IReadOnlyList<string>>(),
            },
        };

        var generated = Generator.New(Repository.Catalog, packs, "shop", profile, ["backend.database=postgres"]);
        generated.Errors.ShouldBeEmpty();

        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);
        List<string> projects = [.. files.Keys.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        List<string> listed = [.. ProjectPath().Matches(files["Solution.slnx"]).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal)];

        projects.ShouldContain("src/Modules/Reports/Reports.Domain/Reports.Domain.csproj");
        projects.ShouldContain("tests/Modules/Reports.Tests/Reports.Tests.csproj");
        var missing = projects.Except(listed, StringComparer.Ordinal).ToList();
        var unknown = listed.Except(projects, StringComparer.Ordinal).ToList();
        missing.ShouldBeEmpty();
        unknown.ShouldBeEmpty();
    }

    [GeneratedRegex("<Project Path=\"([^\"]+)\" />")]
    private static partial Regex ProjectPath();
}

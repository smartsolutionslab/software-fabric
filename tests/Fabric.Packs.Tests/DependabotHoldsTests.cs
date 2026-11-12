using System.Text.RegularExpressions;
using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// A package the generator holds back on purpose is one Dependabot must not propose (#613): the grouped NuGet pull
/// request would take it past the hold and fail restore, and every other minor and patch update in the group with it.
/// So every held version in a generated <c>Directory.Packages.props</c> has an <c>ignore</c> entry in the NuGet entry of
/// <c>.github/dependabot.yml</c>, contributed by the pack that pins it, with a comment naming the issue that lifts the
/// hold; a project that does not pin the package has no entry for it, and nothing else is ignored.
/// </summary>
public sealed partial class DependabotHoldsTests
{
    /// <summary>
    /// The holds: Roslyn at 5.0.0, which Wolverine's runtime compiler requires exactly (#596); EF Core at the build
    /// Npgsql 11.0.0-rc.3 requires, in PostgreSQL projects only, until Npgsql 11 ships (#550, #551).
    /// </summary>
    private static readonly (string Package, string Version)[] Held =
    [
        ("Microsoft.CodeAnalysis.CSharp", "5.0.0"),
        ("Microsoft.EntityFrameworkCore", "11.0.0-rc.3.26522.148"),
        ("Microsoft.EntityFrameworkCore.Design", "11.0.0-rc.3.26522.148"),
        ("Microsoft.EntityFrameworkCore.Relational", "11.0.0-rc.3.26522.148"),
        ("Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore", "11.0.0-rc.3.26522.148"),
    ];

    /// <summary>A held package admits no newer version at all, so every kind of update is ignored.</summary>
    private static readonly string[] EveryUpdateType = ["version-update:semver-major", "version-update:semver-minor", "version-update:semver-patch"];

    /// <summary>Every profile, and lean-crud on SQL Server, whose EF Core is not held.</summary>
    public static TheoryData<string, string> Projects
    {
        get
        {
            TheoryData<string, string> projects = [];
            foreach (var profile in Repository.Catalog.Profiles.Keys)
            {
                projects.Add(profile, "");
            }

            projects.Add("lean-crud", "backend.database=sqlserver");
            return projects;
        }
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_held_package_version_has_an_ignore_entry_for_every_update_and_nothing_else_is_ignored(string profile, string choice)
    {
        var (packages, dependabot) = Generate(profile, choice);

        var held = Held
            .Where(hold => PackageVersions().Matches(packages).Any(match => match.Groups["id"].Value == hold.Package && match.Groups["version"].Value == hold.Version))
            .Select(hold => hold.Package)
            .ToList();
        held.ShouldContain("Microsoft.CodeAnalysis.CSharp");

        var ignored = Ignores(dependabot);
        ignored.Keys.ShouldBe(held, ignoreOrder: true);
        foreach (var (package, updateTypes) in ignored)
        {
            updateTypes.ShouldBe(EveryUpdateType, ignoreOrder: true, package);
        }
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_ignore_entry_says_why_and_names_the_generator_issue_that_lifts_the_hold(string profile, string choice)
    {
        var (_, dependabot) = Generate(profile, choice);
        var lines = dependabot.Split('\n');
        lines.ShouldContain(line => line.TrimStart().StartsWith("- dependency-name:", StringComparison.Ordinal), "every project holds Roslyn back, so it has an ignore entry");

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].TrimStart().StartsWith("- dependency-name:", StringComparison.Ordinal))
            {
                continue;
            }

            // The comment above the entry, or above the run of entries it heads.
            var above = index - 1;
            while (above >= 0 && !lines[above].TrimStart().StartsWith('#') && lines[above].StartsWith("      ", StringComparison.Ordinal))
            {
                above--;
            }

            List<string> comment = [];
            while (above >= 0 && lines[above].TrimStart().StartsWith('#'))
            {
                comment.Insert(0, lines[above].Trim());
                above--;
            }

            var why = string.Join(' ', comment);
            why.ShouldNotBeEmpty($"{lines[index].Trim()} has no comment saying why it is held");
            GeneratorIssue().IsMatch(why).ShouldBeTrue($"{lines[index].Trim()}: '{why}' names no smartsolutionslab/software-fabric issue that lifts the hold");
        }
    }

    private static (string Packages, string Dependabot) Generate(string profile, string choice)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, choice.Length == 0 ? [] : [choice]);
        generated.Errors.ShouldBeEmpty();

        return (
            generated.Files.Single(file => file.Path == "Directory.Packages.props").Content,
            generated.Files.Single(file => file.Path == ".github/dependabot.yml").Content);
    }

    /// <summary>The NuGet entry's ignore list: each dependency name with its update types.</summary>
    private static Dictionary<string, List<string>> Ignores(string dependabot)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(dependabot));
        var updates = (YamlSequenceNode)((YamlMappingNode)stream.Documents[0].RootNode).Children[new YamlScalarNode("updates")];
        var nuget = updates.Children.Cast<YamlMappingNode>().Single(entry => entry.Children[new YamlScalarNode("package-ecosystem")].ToString() == "nuget");
        if (!nuget.Children.TryGetValue(new YamlScalarNode("ignore"), out var ignore))
        {
            return [];
        }

        return ((YamlSequenceNode)ignore).Children.Cast<YamlMappingNode>().ToDictionary(
            entry => entry.Children[new YamlScalarNode("dependency-name")].ToString(),
            entry => entry.Children.TryGetValue(new YamlScalarNode("update-types"), out var types)
                ? ((YamlSequenceNode)types).Children.Select(type => type.ToString()).ToList()
                : []);
    }

    [GeneratedRegex("""<PackageVersion Include="(?<id>[^"]+)" Version="(?<version>[^"]+)" />""")]
    private static partial Regex PackageVersions();

    [GeneratedRegex(@"smartsolutionslab/software-fabric#\d+")]
    private static partial Regex GeneratorIssue();
}

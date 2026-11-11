using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Generated projects take Aspire 13.6.5 and the 10.11.0 release of the Microsoft.Extensions packages the base pins
/// (#562): every Aspire package, the AppHost SDK and the Aspire CLI that sets up its bundle, in the packs and in this
/// repository's own workflows, which set the bundle up for the projects they generate. Community Toolkit integrations
/// are versioned on their own and are not Aspire's.
/// </summary>
public sealed partial class AspireReleaseTests
{
    private const string Aspire = "13.6.5";
    private const string Extensions = "10.11.0";

    private static readonly string[] ExtensionsPackages =
    [
        "Microsoft.Extensions.Http.Resilience",
        "Microsoft.Extensions.ServiceDiscovery",
        "Microsoft.Extensions.TimeProvider.Testing",
    ];

    private static readonly string[] Scanned = ["packs", ".github"];

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    [Fact]
    public void No_pack_or_workflow_names_an_Aspire_version_other_than_13_6_5()
    {
        var others = Scanned
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(Repository.Root, directory), "*", SearchOption.AllDirectories))
            .SelectMany(path => AspireVersion().Matches(File.ReadAllText(path))
                .Where(match => match.Groups["version"].Value != Aspire)
                .Select(match => $"{Path.GetRelativePath(Repository.Root, path)}: {match.Value}"))
            .ToList();

        others.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_profile_takes_Aspire_13_6_5_and_the_Microsoft_Extensions_10_11_0_packages(string profile)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);
        var versions = PackageVersion().Matches(files["Directory.Packages.props"])
            .ToDictionary(match => match.Groups["id"].Value, match => match.Groups["version"].Value);

        foreach (var package in ExtensionsPackages)
        {
            versions[package].ShouldBe(Extensions, package);
        }

        versions.Where(version => version.Key.StartsWith("Aspire.", StringComparison.Ordinal))
            .ShouldAllBe(version => version.Value == Aspire);
        files["src/AppHost/AppHost.csproj"].ShouldStartWith($"<Project Sdk=\"Aspire.AppHost.Sdk/{Aspire}\">");
        files[".github/workflows/ci.yml"].ShouldContain($"aspire.cli@{Aspire} ");
    }

    [GeneratedRegex("""<PackageVersion Include="(?<id>[^"]+)" Version="(?<version>[^"]+)" />""")]
    private static partial Regex PackageVersion();

    [GeneratedRegex("""(?<![\w.])(?:Aspire\.[A-Za-z.]+" Version="|Aspire\.AppHost\.Sdk/|aspire\.cli@)(?<version>[0-9][^"\s]*)""")]
    private static partial Regex AspireVersion();
}

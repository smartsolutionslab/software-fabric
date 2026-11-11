using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The NuGet minor and patch releases of #588, one family at a time so a breakage points at its family: every pin of a
/// package in the packs, and the generator's own in <c>Directory.Packages.props</c>, names the release below, and so
/// does every profile's generated <c>Directory.Packages.props</c>. StyleCop and Npgsql are not part of it.
/// <c>Microsoft.CodeAnalysis.CSharp</c> is held at 5.0.0: Wolverine 6.64.0 takes <c>JasperFx.RuntimeCompiler</c> 5.1.0,
/// whose Roslyn packages require exactly 5.0.0, so 5.9.0 fails the restore of every project with Wolverine (NU1608). It
/// moves when Wolverine takes a runtime compiler built on a later Roslyn.
/// </summary>
public sealed partial class NuGetReleaseTests
{
    private const string GeneratorFamily = "generator";

    private static readonly (string Family, string Package, string Version)[] Releases =
    [
        ("wolverine", "WolverineFx", "6.64.0"),
        ("wolverine", "WolverineFx.EntityFrameworkCore", "6.64.0"),
        ("wolverine", "WolverineFx.RuntimeCompilation", "6.64.0"),
        ("wolverine", "WolverineFx.Postgresql", "6.64.0"),
        ("wolverine", "WolverineFx.SqlServer", "6.64.0"),
        ("wolverine", "WolverineFx.RabbitMQ", "6.64.0"),
        ("wolverine", "WolverineFx.AzureServiceBus", "6.64.0"),
        ("wolverine", "WolverineFx.Kafka", "6.64.0"),
        ("marten", "Marten", "9.59.0"),
        ("opentelemetry", "OpenTelemetry.Exporter.OpenTelemetryProtocol", "1.21.0"),
        ("opentelemetry", "OpenTelemetry.Extensions.Hosting", "1.21.0"),
        ("opentelemetry", "OpenTelemetry.Instrumentation.AspNetCore", "1.20.2"),
        ("opentelemetry", "OpenTelemetry.Instrumentation.Http", "1.20.2"),
        ("opentelemetry", "OpenTelemetry.Instrumentation.Runtime", "1.20.1"),
        ("ai", "Anthropic", "13.8.0"),
        ("ai", "Microsoft.Agents.AI", "1.28.0"),
        ("ai", "ModelContextProtocol.Core", "2.4.0"),
        ("ai", "ModelContextProtocol.AspNetCore", "2.4.0"),
        ("ai", "OllamaSharp", "5.5.2"),
        ("identity", "Microsoft.IdentityModel.JsonWebTokens", "8.23.0"),
        ("testing", "TUnit.Engine", "1.83.0"),
        ("testing", "xunit.v3", "4.0.3"),
        ("held", "Microsoft.CodeAnalysis.CSharp", "5.0.0"),
        (GeneratorFamily, "Scriban", "7.6.1"),
        (GeneratorFamily, "JsonSchema.Net", "9.6.0"),
        (GeneratorFamily, "xunit.v3", "4.0.3"),
    ];

    public static TheoryData<string> Families => [.. Releases.Select(release => release.Family).Distinct()];

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    [Theory]
    [MemberData(nameof(Families))]
    public void Every_pin_of_the_family_names_its_release(string family)
    {
        string[] files = family == GeneratorFamily
            ? [Path.Combine(Repository.Root, "Directory.Packages.props")]
            : [.. Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*", SearchOption.AllDirectories)];
        var releases = Releases.Where(release => release.Family == family).ToDictionary(release => release.Package, release => release.Version);

        var pins = files
            .SelectMany(path => PackageVersion().Matches(File.ReadAllText(path))
                .Where(match => releases.ContainsKey(match.Groups["id"].Value))
                .Select(match => (Path: Path.GetRelativePath(Repository.Root, path), Package: match.Groups["id"].Value, Version: match.Groups["version"].Value)))
            .ToList();

        pins.Select(pin => pin.Package).Distinct().ShouldBe(releases.Keys, ignoreOrder: true);
        pins.Where(pin => pin.Version != releases[pin.Package])
            .Select(pin => $"{pin.Path}: {pin.Package} {pin.Version}")
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_profile_takes_the_releases(string profile)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
        generated.Errors.ShouldBeEmpty();
        var releases = Releases.Where(release => release.Family != GeneratorFamily).ToDictionary(release => release.Package, release => release.Version);

        PackageVersion().Matches(generated.Files.Single(file => file.Path == "Directory.Packages.props").Content)
            .Where(match => releases.TryGetValue(match.Groups["id"].Value, out var release) && match.Groups["version"].Value != release)
            .Select(match => match.Value)
            .ShouldBeEmpty();
    }

    [GeneratedRegex("""Include="(?<id>[^"]+)" (?:Version|VersionOverride)="(?<version>[^"]+)(?=")""")]
    private static partial Regex PackageVersion();
}

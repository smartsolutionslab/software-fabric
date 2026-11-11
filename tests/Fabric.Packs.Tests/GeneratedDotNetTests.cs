using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Generated projects are .NET 11 projects (#550): SDK 11.0.100, net11.0, and the 11.0.0 release of every Microsoft
/// package that has one. A PostgreSQL project takes EF Core 11.0.0-rc.3.26522.148 instead, the exact build that
/// Npgsql.EntityFrameworkCore.PostgreSQL 11.0.0-rc.3 requires, until Npgsql 11.0.0 ships (#551).
/// </summary>
public sealed partial class GeneratedDotNetTests
{
    private const string Release = "11.0.0";
    private const string EfCoreForNpgsql = "11.0.0-rc.3.26522.148";

    private static readonly string[] EfCore =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore",
    ];

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    public static TheoryData<string> PostgresProfiles => [.. Repository.ProfilesChoosing("backend.database", "postgres")];

    public static TheoryData<string, string[]> AuthenticationPackages => new()
    {
        { "keycloak", ["Microsoft.AspNetCore.Authentication.JwtBearer", "Microsoft.AspNetCore.Mvc.Testing"] },
        { "entra-id", ["Microsoft.AspNetCore.Authentication.JwtBearer", "Microsoft.AspNetCore.Mvc.Testing"] },
        { "generic-oidc", ["Microsoft.AspNetCore.Authentication.JwtBearer", "Microsoft.AspNetCore.Mvc.Testing"] },
        { "none", ["Microsoft.AspNetCore.Mvc.Testing"] },
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_profile_pins_SDK_11_and_targets_net11_0(string profile)
    {
        var files = Files(profile);

        ((string?)JsonNode.Parse(files["global.json"])!["sdk"]?["version"]).ShouldBe("11.0.100");
        files["Directory.Build.props"].ShouldContain("<TargetFramework>net11.0</TargetFramework>");
    }

    [Fact]
    public void No_pack_template_names_a_target_framework_other_than_net11_0()
    {
        var others = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*", SearchOption.AllDirectories)
            .SelectMany(path => TargetFrameworkName().Matches(File.ReadAllText(path))
                .Where(match => match.Value != "net11.0")
                .Select(match => $"{Path.GetRelativePath(Repository.Root, path)}: {match.Value}"))
            .ToList();

        others.ShouldBeEmpty();
    }

    [Fact]
    public void No_pack_runs_a_generated_project_in_a_dotnet_image_other_than_11_0()
    {
        var others = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*", SearchOption.AllDirectories)
            .SelectMany(path => DotNetImage().Matches(File.ReadAllText(path))
                .Where(match => match.Groups["tag"].Value != "11.0")
                .Select(match => $"{Path.GetRelativePath(Repository.Root, path)}: {match.Value}"))
            .ToList();

        others.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PostgresProfiles))]
    public void A_postgres_profile_takes_the_EF_Core_build_Npgsql_11_0_0_rc_3_requires(string profile)
    {
        var versions = PackageVersions(Files(profile));

        versions["Npgsql.EntityFrameworkCore.PostgreSQL"].ShouldBe("11.0.0-rc.3");
        foreach (var package in EfCore)
        {
            versions[package].ShouldBe(EfCoreForNpgsql, package);
        }

        versions["Microsoft.AspNetCore.OpenApi"].ShouldBe(Release);
    }

    [Fact]
    public void A_SQL_Server_project_takes_EF_Core_11_0_0()
    {
        var versions = PackageVersions(Files("lean-crud", "backend.database=sqlserver"));

        versions.ShouldNotContainKey("Npgsql.EntityFrameworkCore.PostgreSQL");
        foreach (var package in EfCore.Append("Microsoft.EntityFrameworkCore.SqlServer").Append("System.Security.Cryptography.Pkcs"))
        {
            versions[package].ShouldBe(Release, package);
        }
    }

    [Theory]
    [MemberData(nameof(AuthenticationPackages))]
    public void Every_authentication_option_takes_the_ASP_NET_Core_11_0_0_packages(string auth, string[] packages)
    {
        var versions = PackageVersions(Files("lean-crud", $"backend.auth={auth}"));

        foreach (var package in packages)
        {
            versions[package].ShouldBe(Release, package);
        }
    }

    [Fact]
    public void The_SignalR_client_is_the_11_0_0_release()
    {
        PackageVersions(Files("lean-crud", "frontend.realtime=signalr"))["Microsoft.AspNetCore.SignalR.Client"].ShouldBe(Release);
    }

    [Fact]
    public void The_dotnet_ef_tool_is_the_11_0_0_release_in_a_postgres_project_too()
    {
        var tools = JsonNode.Parse(Files("lean-crud")[".config/dotnet-tools.json"])!;

        ((string?)tools["tools"]?["dotnet-ef"]?["version"]).ShouldBe(Release);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static Dictionary<string, string> PackageVersions(Dictionary<string, string> files) =>
        PackageVersion().Matches(files["Directory.Packages.props"]).ToDictionary(match => match.Groups["id"].Value, match => match.Groups["version"].Value);

    [GeneratedRegex("""<PackageVersion Include="(?<id>[^"]+)" Version="(?<version>[^"]+)" />""")]
    private static partial Regex PackageVersion();

    [GeneratedRegex(@"mcr\.microsoft\.com/dotnet/[a-z-]+:(?<tag>[0-9][^""\s]*)")]
    private static partial Regex DotNetImage();

    [GeneratedRegex(@"\bnet\d+\.\d+\b")]
    private static partial Regex TargetFrameworkName();
}

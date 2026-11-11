using System.Text.Json;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>web.pnpm-overrides</c>, a role slot of <c>frontend.workspace</c> (#569): pnpm reads <c>overrides</c> only from the
/// workspace root, which the workspace pack owns, so a pack that must pin a transitive dependency (#550) contributes an
/// entry, a package name and its version, here. Without a contribution the root's package.json is what it always was.
/// </summary>
public sealed class PnpmOverridesSlotTests
{
    private const string Profile = "saas-standard-react";
    private const string RootPackage = "web/package.json";

    public static readonly TheoryData<string> Workspaces = new() { "pnpm-workspaces", "nx", "turborepo" };

    [Theory]
    [MemberData(nameof(Workspaces))]
    public void An_override_another_pack_contributes_becomes_the_roots_pnpm_override(string workspace)
    {
        using var probe = Probe();
        var packs = Repository.Packs.Packs.ToDictionary();
        packs[probe.Manifest.Option] = probe.Manifest;

        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, ["frontend.pwa=yes", $"frontend.workspace={workspace}"]);
        generated.Errors.ShouldBeEmpty();

        var package = generated.Files.Single(file => file.Path == RootPackage).Content;
        using var json = JsonDocument.Parse(package);
        json.RootElement.GetProperty("pnpm").GetProperty("overrides").GetProperty("picocolors").GetString().ShouldBe("1.1.0");
        package.ShouldEndWith("""
              "pnpm": {
                "overrides": {
                  "picocolors": "1.1.0"
                }
              }
            }

            """);
    }

    [Theory]
    [MemberData(nameof(Workspaces))]
    public void Without_a_contribution_the_root_has_no_pnpm_settings(string workspace)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [$"frontend.workspace={workspace}"]);
        generated.Errors.ShouldBeEmpty();

        var package = generated.Files.Single(file => file.Path == RootPackage).Content;
        package.ShouldNotContain("\"pnpm\"");
        package.ShouldNotContain("overrides");
    }

    /// <summary>
    /// What a pack relies on: the pin reaches the install. postcss and vite ask for picocolors ^1.1.1; with the override
    /// every one of them gets 1.1.0, and no other version is resolved.
    /// </summary>
    [Theory]
    [MemberData(nameof(Workspaces))]
    public void With_a_contribution_pnpm_install_resolves_the_pinned_version(string workspace)
    {
        using var probe = Probe();
        using var project = new GeneratedProject(Profile, [probe.Manifest], "frontend.pwa=yes", $"frontend.workspace={workspace}");
        var web = Path.Combine(project.ProjectDirectory, "web");

        var (exitCode, output) = Shell.Run(web, "pnpm", "install");
        exitCode.ShouldBe(0, $"pnpm install:\n{output}");

        var lockfile = File.ReadAllText(Path.Combine(web, "pnpm-lock.yaml"));
        var resolved = Regex.Matches(lockfile, @"^  picocolors@([^:\s]+):", RegexOptions.Multiline).Select(match => match.Groups[1].Value).Distinct().ToList();
        resolved.ShouldBe(["1.1.0"], customMessage: lockfile);
    }

    /// <summary>Stands in for frontend.pwa.yes, contributing only an override.</summary>
    private static ProbePack Probe() => new("frontend.pwa.yes", new Dictionary<string, string>
    {
        ["web.pnpm-overrides"] = "      \"picocolors\": \"1.1.0\"",
    });
}

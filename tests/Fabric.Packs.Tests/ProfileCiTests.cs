using System.Text.Json;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Profile CI (ADR-0003) runs every profile whose packs all exist: build/complete-profiles.sh lists them for the
/// workflow's matrix, and says which profiles it leaves out and why.
/// </summary>
public sealed class ProfileCiTests
{
    private static readonly string[] CatalogProfiles = [.. Repository.Catalog.Profiles.Keys];

    /// <summary>The profiles the real packs generate alone, without stand-ins.</summary>
    private static readonly string[] CompleteProfiles =
        [.. CatalogProfiles.Where(profile => Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []).Errors.Count == 0)];

    private static readonly Lazy<(int ExitCode, string Output)> Script = new(() => Shell.Run(Repository.Root, "bash", "build/complete-profiles.sh"));

    [Fact]
    public void The_script_lists_exactly_the_profiles_the_real_packs_generate()
    {
        var (exitCode, output) = Script.Value;

        exitCode.ShouldBe(0, output);
        Listed(output).ShouldBe(CompleteProfiles, output);
        CompleteProfiles.ShouldContain("saas-standard-react");
        CompleteProfiles.ShouldContain("lean-crud");
    }

    [Fact]
    public void A_profile_left_out_is_named_with_the_packs_it_lacks()
    {
        var (_, output) = Script.Value;
        foreach (var profile in CatalogProfiles.Except(CompleteProfiles))
        {
            output.ShouldContain($"::notice title=profile CI::{profile} is not run, its packs are not all written: no pack for ", Case.Sensitive, output);
        }
    }

    [Fact]
    public void The_workflow_runs_profile_CI_for_the_listed_profiles_and_names_none_itself()
    {
        var workflow = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml"));

        workflow.ShouldContain("build/complete-profiles.sh");
        workflow.ShouldContain("build/profile-ci.sh \"${{ matrix.profile }}\"");
        foreach (var profile in CatalogProfiles)
        {
            workflow.ShouldNotContain($"profile-ci.sh {profile}", Case.Sensitive, "the workflow names a profile; the matrix comes from build/complete-profiles.sh.");
        }
    }

    /// <summary>The <c>profiles=[...]</c> line the script writes for <c>$GITHUB_OUTPUT</c>.</summary>
    private static string[] Listed(string output)
    {
        const string prefix = "profiles=";
        var line = output.Split('\n').SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        line.ShouldNotBeNull($"no '{prefix}' line in:\n{output}");
        return JsonSerializer.Deserialize<string[]>(line[prefix.Length..])!;
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Profile CI (ADR-0003) runs every profile whose packs all exist: build/complete-profiles.sh lists them for the
/// workflow's matrix, and says which profiles it leaves out and why. build/profile-ci.sh takes overrides, so a pack
/// can go through profile CI before its profile is complete.
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
        CompleteProfiles.ShouldContain("saas-strict-react");
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

    [Fact]
    public void An_override_is_what_profile_CI_generates()
    {
        var (_, output, fabricYaml) = ProfileCiUntilItsFirstStep("saas-standard-react", "--set", "module.ddd-depth=strict");

        output.ShouldContain("::group::saas-standard-react --set module.ddd-depth=strict: ", Case.Sensitive, output);
        fabricYaml.ShouldNotBeNull(output);
        fabricYaml.ShouldContain("  module.ddd-depth: strict\n", Case.Sensitive, output);
    }

    [Fact]
    public void An_override_the_catalog_refuses_stops_profile_CI_before_its_first_step()
    {
        var (exitCode, output, _) = ProfileCiUntilItsFirstStep("saas-standard-react", "--set", "frontend.design-system=no-such-option");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'no-such-option' is not an option of frontend.design-system", Case.Sensitive, output);
        output.ShouldNotContain("::group::", Case.Sensitive, output);
    }

    [Fact]
    public void An_argument_that_is_not_an_override_is_refused()
    {
        var (exitCode, output, _) = ProfileCiUntilItsFirstStep("saas-standard-react", "module.ddd-depth=strict");

        exitCode.ShouldBe(2, output);
        output.ShouldContain("Usage: build/profile-ci.sh <profile> [--set dimension=option ...]", Case.Sensitive, output);
        output.ShouldNotContain("::group::", Case.Sensitive, output);
    }

    /// <summary>
    /// Runs build/profile-ci.sh only as far as its first CI step, so a test sees what it generated without running the
    /// generated project's CI. Its work directory is made in a temporary directory of the test's own, so the generated
    /// project's <c>fabric.yaml</c> can be read when the script got that far, and nothing is left behind.
    /// </summary>
    private static (int ExitCode, string Output, string? FabricYaml) ProfileCiUntilItsFirstStep(params string[] arguments)
    {
        var temporary = Directory.CreateTempSubdirectory("profile-ci-test-");
        try
        {
            var start = new ProcessStartInfo("bash") { WorkingDirectory = Repository.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("build/profile-ci.sh");
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["TMPDIR"] = temporary.FullName;
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = new StringBuilder();
            var stepped = false;
            while (process.StandardOutput.ReadLine() is { } line)
            {
                output.AppendLine(line);
                if (line.StartsWith("::group::", StringComparison.Ordinal))
                {
                    stepped = true;
                    process.Kill(entireProcessTree: true);
                    break;
                }
            }

            process.WaitForExit();
            var fabricYaml = Directory.GetDirectories(temporary.FullName)
                .Select(work => Path.Combine(work, "shop", "fabric.yaml"))
                .Where(File.Exists)
                .Select(File.ReadAllText)
                .SingleOrDefault();
            return (stepped ? -1 : process.ExitCode, output + error.Result, fabricYaml);
        }
        finally
        {
            temporary.Delete(recursive: true);
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

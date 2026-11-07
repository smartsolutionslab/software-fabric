using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Fabric.Core.Choices;
using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// Profile CI (ADR-0003) runs every profile in the catalog: build/complete-profiles.sh lists them for the workflow's
/// matrix, and fails, naming the profile and the packs it lacks, when one cannot be generated from the real packs. build/profile-ci.sh takes overrides, so a pack
/// can go through profile CI before its profile is complete.
/// </summary>
public sealed class ProfileCiTests
{
    /// <summary>
    /// Stands for a matrix in build/matrix.json's shape among a test's arguments: the run writes <see cref="MatrixJson"/>
    /// to its temporary directory and passes that path instead.
    /// </summary>
    private const string Matrix = "<matrix.json>";

    private const string MatrixJson = """
        {
          "combinations": [
            { "name": "pair-1", "choices": { "backend.llm": "openai", "module.ddd-depth": "strict" } },
            { "name": "pair-2", "choices": { "module.ddd-depth": "light" } }
          ]
        }
        """;

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
        CompleteProfiles.ShouldBe(CatalogProfiles, "every profile in the catalog is one profile CI runs.");
    }

    [Fact]
    public void A_profile_with_a_missing_pack_fails_profile_CI_naming_the_profile_and_the_pack()
    {
        var (exitCode, output) = CompleteProfilesWithout(Path.Combine("frontend", "mfe-technique", "module-federation"));

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("::error title=profile CI::industrial-event-driven-mfe ", Case.Sensitive, output);
        output.ShouldContain("no pack for frontend.mfe-technique=module-federation", Case.Sensitive, output);
        output.ShouldNotContain("::notice", Case.Sensitive, output);
        output.ShouldNotContain("profiles=", Case.Sensitive, output);
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
    public void Without_a_list_of_changes_every_profile_is_affected()
    {
        var (exitCode, output) = Script.Value;

        exitCode.ShouldBe(0, output);
        Listed(output, "affected").ShouldBe(CompleteProfiles, output);
    }

    [Fact]
    public void A_change_to_frontend_packs_affects_only_the_profiles_that_choose_one_of_them()
    {
        string[] expected = [.. CatalogProfiles.Where(profile => Selects(profile, "frontend.state.redux-toolkit") || Selects(profile, "frontend.framework.vue"))];

        var (exitCode, output) = CompleteProfilesAfter("packs/frontend/state/redux-toolkit/templates/store.ts.sbn", "packs/frontend/framework/vue/pack.yaml");

        exitCode.ShouldBe(0, output);
        expected.ShouldNotBeEmpty();
        expected.Length.ShouldBeLessThan(CatalogProfiles.Length, "the packs must leave a profile out, or the test proves nothing.");
        Listed(output, "affected").ShouldBe(expected, output);
        Listed(output, "profiles").ShouldBe(CompleteProfiles, "every profile is still listed, so every profile job reports.");
    }

    [Fact]
    public void A_change_to_a_module_pack_affects_the_profiles_whose_module_defaults_choose_it()
    {
        string[] expected = [.. CatalogProfiles.Where(profile => Selects(profile, "module.ddd-depth.strict"))];

        var (exitCode, output) = CompleteProfilesAfter("packs/module/ddd-depth/strict/docs/adr.md");

        exitCode.ShouldBe(0, output);
        expected.ShouldNotBeEmpty();
        expected.Length.ShouldBeLessThan(CatalogProfiles.Length);
        Listed(output, "affected").ShouldBe(expected, output);
    }

    [Fact]
    public void A_change_to_a_pack_no_profile_chooses_affects_none()
    {
        var unchosen = Repository.Packs.Packs.Values
            .Where(pack => !pack.IsBase)
            .Select(pack => pack.Option)
            .First(option => !CatalogProfiles.Any(profile => Selects(profile, option)));

        var (exitCode, output) = CompleteProfilesAfter($"packs/{unchosen.Replace('.', '/')}/pack.yaml");

        exitCode.ShouldBe(0, output);
        Listed(output, "affected").ShouldBeEmpty(output);
    }

    [Fact]
    public void A_change_to_documentation_or_the_generator_s_tests_affects_no_profile()
    {
        var (exitCode, output) = CompleteProfilesAfter("docs/roadmap.md", "README.md", "tests/Fabric.Packs.Tests/ProfileCiTests.cs");

        exitCode.ShouldBe(0, output);
        Listed(output, "affected").ShouldBeEmpty(output);
    }

    [Theory]
    [InlineData("src/Fabric.Core/Generation/Generator.cs")]
    [InlineData("build/profile-ci.sh")]
    [InlineData("catalog/catalog.yaml")]
    [InlineData("packs/base/core/templates/README.md.sbn")]
    [InlineData(".github/workflows/ci.yml")]
    public void A_change_outside_the_option_packs_affects_every_profile(string path)
    {
        var (exitCode, output) = CompleteProfilesAfter("packs/frontend/state/redux-toolkit/pack.yaml", path);

        exitCode.ShouldBe(0, output);
        Listed(output, "affected").ShouldBe(CompleteProfiles, output);
        output.ShouldContain($"every profile is affected: {path} changed", Case.Sensitive, output);
    }

    [Fact]
    public void The_workflow_lists_the_affected_profiles_and_an_unaffected_profile_job_runs_none_of_its_steps()
    {
        var jobs = (YamlMappingNode)Workflow()["jobs"];
        var list = ((YamlSequenceNode)((YamlMappingNode)jobs["profiles"])["steps"]).Children.Cast<YamlMappingNode>()
            .Single(step => step.Children.TryGetValue(new YamlScalarNode("id"), out var id) && ((YamlScalarNode)id).Value == "list");
        var profile = (YamlMappingNode)jobs["profile"];
        var matrix = (YamlMappingNode)((YamlMappingNode)profile["strategy"])["matrix"];
        var affected = ((YamlScalarNode)((YamlMappingNode)profile["env"])["AFFECTED"]).Value!;
        var steps = ((YamlSequenceNode)profile["steps"]).Children.Cast<YamlMappingNode>().ToList();

        ((YamlScalarNode)list["run"]).Value!.ShouldContain("build/complete-profiles.sh --changed ");
        ((YamlScalarNode)matrix["profile"]).Value.ShouldBe("${{ fromJSON(needs.profiles.outputs.complete) }}", "every profile job appears, so every one reports.");
        affected.ShouldBe("${{ contains(fromJSON(needs.profiles.outputs.affected), matrix.profile) }}");
        foreach (var step in steps)
        {
            step.Children.ShouldContainKey(new YamlScalarNode("if"), $"a profile job step without an if runs for an unaffected profile: {step}");
            ((YamlScalarNode)step["if"]).Value.ShouldStartWith("env.AFFECTED ");
        }

        steps.Count(step => ((YamlScalarNode)step["if"]).Value == "env.AFFECTED == 'true'").ShouldBe(steps.Count - 1);
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

    [Fact]
    public void A_matrix_combination_is_what_profile_CI_generates_with_no_profile()
    {
        var (_, output, fabricYaml) = ProfileCiUntilItsFirstStep("--combination", "pair-1", "--matrix", Matrix);

        output.ShouldContain("::group::combination pair-1: backend format check", Case.Sensitive, output);
        fabricYaml.ShouldNotBeNull(output);
        fabricYaml.ShouldNotContain("profile:", Case.Sensitive, output);
        fabricYaml.ShouldContain("  module.ddd-depth: strict\n", Case.Sensitive, output);
        fabricYaml.ShouldContain("  backend.llm: openai\n", Case.Sensitive, output);
    }

    [Fact]
    public void Profile_CI_runs_only_the_steps_it_is_given()
    {
        var (exitCode, output, fabricYaml) = ProfileCiUntilItsFirstStep("--combination", "pair-1", "--matrix", Matrix, "--steps", "generate");

        exitCode.ShouldBe(0, output);
        output.ShouldNotContain("::group::", Case.Sensitive, output);
        output.ShouldContain("combination pair-1: generated\n", Case.Sensitive, output);
        fabricYaml.ShouldBeNull("the work directory is removed when the run ends.");
    }

    [Fact]
    public void A_combination_the_matrix_lacks_stops_profile_CI_before_its_first_step()
    {
        var (exitCode, output, _) = ProfileCiUntilItsFirstStep("--combination", "pair-9", "--matrix", Matrix);

        exitCode.ShouldBe(1, output);
        output.ShouldContain("no combination pair-9 in ", Case.Sensitive, output);
        output.ShouldContain("matrix.json", Case.Sensitive, output);
        output.ShouldNotContain("::group::", Case.Sensitive, output);
    }

    [Theory]
    [InlineData("generate,deploy", "unknown step deploy; the steps are generate,build,test,web,e2e")]
    [InlineData("build,test", "every run starts with generate")]
    [InlineData("generate,test", "test needs build")]
    [InlineData("generate,build,e2e", "e2e needs web")]
    public void Steps_profile_CI_cannot_run_are_refused_before_generating(string steps, string message)
    {
        var (exitCode, output, fabricYaml) = ProfileCiUntilItsFirstStep("saas-standard-react", "--steps", steps);

        exitCode.ShouldBe(2, output);
        output.ShouldContain(message, Case.Sensitive, output);
        fabricYaml.ShouldBeNull(output);
    }

    [Fact]
    public void A_profile_and_a_combination_together_are_refused()
    {
        var (exitCode, output, _) = ProfileCiUntilItsFirstStep("saas-standard-react", "--combination", "pair-1", "--matrix", Matrix);

        exitCode.ShouldBe(2, output);
        output.ShouldContain("Usage: build/profile-ci.sh --combination <name>", Case.Sensitive, output);
    }

    /// <summary>
    /// Runs build/profile-ci.sh only as far as its first CI step, so a test sees what it generated without running the
    /// generated project's CI. Its work directory is made in a temporary directory of the test's own, so the generated
    /// project's <c>fabric.yaml</c> can be read when the script got that far, and nothing is left behind. The test
    /// matrix is written there too.
    /// </summary>
    private static (int ExitCode, string Output, string? FabricYaml) ProfileCiUntilItsFirstStep(params string[] arguments)
    {
        var temporary = Directory.CreateTempSubdirectory("profile-ci-test-");
        try
        {
            var start = new ProcessStartInfo("bash") { WorkingDirectory = Repository.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("build/profile-ci.sh");
            var matrix = Path.Combine(temporary.FullName, "matrix.json");
            File.WriteAllText(matrix, MatrixJson);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument == Matrix ? matrix : argument);
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

    /// <summary>
    /// Runs build/complete-profiles.sh from a copy of itself and the Release CLI in a temporary directory, with one
    /// pack directory removed from the packs the copied CLI ships, so the script sees a profile whose packs are not
    /// all written.
    /// </summary>
    private static (int ExitCode, string Output) CompleteProfilesWithout(string pack)
    {
        var copy = Directory.CreateTempSubdirectory("complete-profiles-test-");
        try
        {
            var cli = Path.Combine("src", "Fabric.Cli", "bin", "Release", "net10.0");
            Copy(Path.Combine(Repository.Root, "build", "complete-profiles.sh"), Path.Combine(copy.FullName, "build", "complete-profiles.sh"));
            Copy(Path.Combine(Repository.Root, cli), Path.Combine(copy.FullName, cli));
            var removed = Path.Combine(copy.FullName, cli, "packs", pack);
            Directory.Exists(removed).ShouldBeTrue($"no pack directory {pack} to remove.");
            Directory.Delete(removed, recursive: true);
            return Shell.Run(copy.FullName, "bash", "build/complete-profiles.sh");
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    private static void Copy(string source, string target)
    {
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    /// <summary>Whether <paramref name="profile"/> selects the pack of <paramref name="option"/>, a project choice or a module default.</summary>
    private static bool Selects(string profile, string option)
    {
        var choices = ChoiceResolver.Resolve(Repository.Catalog, profile, []).Choices
            ?? throw new InvalidOperationException($"profile '{profile}' does not resolve.");
        return choices.Project.Concat(choices.ModuleDefaults)
            .Any(choice => choice.Value.Any(chosen => $"{choice.Key}.{chosen}" == option));
    }

    /// <summary>Runs build/complete-profiles.sh with a list of changed paths, as the workflow does on a pull request.</summary>
    private static (int ExitCode, string Output) CompleteProfilesAfter(params string[] changed)
    {
        var temporary = Directory.CreateTempSubdirectory("complete-profiles-changed-");
        try
        {
            var list = Path.Combine(temporary.FullName, "changed");
            File.WriteAllText(list, string.Concat(changed.Select(path => path + "\n")));
            return Shell.Run(Repository.Root, "bash", "build/complete-profiles.sh", "--changed", list);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    private static YamlMappingNode Workflow()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml"))));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    /// <summary>A <c>name=[...]</c> line the script writes for <c>$GITHUB_OUTPUT</c>: <c>profiles</c> or <c>affected</c>.</summary>
    private static string[] Listed(string output, string name = "profiles")
    {
        var prefix = $"{name}=";
        var line = output.Split('\n').SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        line.ShouldNotBeNull($"no '{prefix}' line in:\n{output}");
        return JsonSerializer.Deserialize<string[]>(line[prefix.Length..])!;
    }
}

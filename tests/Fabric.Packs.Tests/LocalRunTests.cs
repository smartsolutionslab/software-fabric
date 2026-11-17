using System.Text.Json;
using System.Text.RegularExpressions;
using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// What a generated project's first local run needs (#691): <c>aspire.config.json</c>, so the Aspire CLI finds the AppHost
/// and leaves nothing untracked; a README that names the prerequisites the chosen packs bring; and <c>global.json</c>'s
/// test runner, without which <c>dotnet test --project</c> is an unknown switch.
/// </summary>
public sealed partial class LocalRunTests
{
    /// <summary>What the Aspire 13.6.5 CLI writes on a first <c>aspire run</c>, with the newline every generated file ends with.</summary>
    private const string AspireConfig = "{\n  \"appHost\": {\n    \"path\": \"src/AppHost/AppHost.csproj\"\n  }\n}\n";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    public static TheoryData<string> Profiles => WorkflowTests.Profiles;

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_profile_points_the_aspire_cli_at_its_AppHost(string profile)
    {
        var files = Files(profile);

        files.ShouldContainKey("aspire.config.json");
        files["aspire.config.json"].ShouldBe(AspireConfig);
        files.ShouldContainKey("src/AppHost/AppHost.csproj");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_readme_names_the_sdk_from_global_json_and_docker_and_both_ways_to_run(string profile)
    {
        var files = Files(profile);
        var section = RunLocally(files["README.md"]);

        using var globalJson = JsonDocument.Parse(files["global.json"]);
        var sdk = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        section.ShouldContain($".NET SDK {sdk}");
        section.ShouldContain("Docker");
        section.ShouldContain("dotnet run --project src/AppHost");
        section.ShouldContain("aspire run");
        section.ShouldContain("aspire doctor");
        section.ShouldContain("aspire.config.json");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void With_a_frontend_the_readme_names_node_and_the_major_of_the_package_manager_the_workspace_pins(string profile)
    {
        var files = Files(profile);
        var section = RunLocally(files["README.md"]);

        using var package = JsonDocument.Parse(files["web/package.json"]);
        var packageManager = PackageManager().Match(package.RootElement.GetProperty("packageManager").GetString()!);
        packageManager.Success.ShouldBeTrue();
        var node = NodeVersion().Match(files[".github/workflows/ci.yml"]);
        node.Success.ShouldBeTrue();

        section.ShouldContain($"Node.js {node.Groups[1].Value}");
        section.ShouldContain($"{packageManager.Groups["name"].Value} {packageManager.Groups["major"].Value}");
        section.ShouldContain("`packageManager`");
    }

    /// <summary>Every catalog profile has a frontend, so stand-ins for every frontend pack show what the README says without one.</summary>
    [Fact]
    public void Without_a_frontend_pack_the_readme_names_neither_node_nor_pnpm()
    {
        using var standIns = new StandIns();
        var resolved = ChoiceResolver.Resolve(Repository.Catalog, "lean-crud", []).Choices!;
        var frontend = resolved.Project
            .Where(entry => entry.Key.StartsWith("frontend.", StringComparison.Ordinal))
            .SelectMany(entry => entry.Value.Select(option => standIns.For($"{entry.Key}.{option}")))
            .ToList();
        frontend.ShouldNotBeEmpty();
        using var project = new GeneratedProject("lean-crud", frontend);

        var section = RunLocally(File.ReadAllText(Path.Combine(project.ProjectDirectory, "README.md")));

        section.ShouldContain("Docker");
        section.ShouldNotContain("Node.js");
        section.ShouldNotContain("pnpm");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_readme_and_agents_md_say_that_global_json_s_test_runner_is_what_makes_dotnet_test_project_work(string profile)
    {
        var files = Files(profile);

        foreach (var file in new[] { "README.md", "AGENTS.md" })
        {
            files[file].ShouldContain("`\"test\": { \"runner\": \"Microsoft.Testing.Platform\" }`", Case.Sensitive, file);
            files[file].ShouldContain("dotnet test --project", Case.Sensitive, file);
            files[file].ShouldContain("MSB1001", Case.Sensitive, file);
            files[file].ShouldContain("GlobalJsonSelectsTheTestingPlatform", Case.Sensitive, file);
        }

        files.ShouldContainKey("tests/Architecture.Tests/Rules/GlobalJsonSelectsTheTestingPlatform.cs");
    }

    /// <summary>
    /// The gate: without the runner, <c>dotnet test --project</c> stops at its arguments before any test runs, so the rule
    /// is seen failing where the test platform runs the tests itself, as an IDE does, here through <c>dotnet run</c>.
    /// </summary>
    [Fact]
    public void A_generated_project_s_architecture_rules_fail_a_global_json_without_the_testing_platform_runner_naming_it()
    {
        using var project = new GeneratedProject("lean-crud");
        project.Build();

        var (freshExitCode, freshOutput) = RunArchitectureTests(project);
        freshExitCode.ShouldBe(0, freshOutput);

        var globalJson = File.ReadAllText(Path.Combine(project.ProjectDirectory, "global.json"));
        var rewritten = globalJson[..globalJson.IndexOf(",\n  \"test\"", StringComparison.Ordinal)] + "\n}\n";
        project.Write("global.json", rewritten);

        var (testExitCode, testOutput) = project.Test(ArchitectureTests);
        testExitCode.ShouldNotBe(0, testOutput);
        testOutput.ShouldContain("MSB1001", Case.Sensitive, testOutput);

        var (exitCode, output) = RunArchitectureTests(project);
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("global.json: test.runner is missing", Case.Sensitive, output);

        project.Write("global.json", globalJson.Replace("Microsoft.Testing.Platform", "VSTest", StringComparison.Ordinal));
        var (vstestExitCode, vstestOutput) = RunArchitectureTests(project);
        vstestExitCode.ShouldNotBe(0, vstestOutput);
        vstestOutput.ShouldContain("global.json: test.runner is \"VSTest\"", Case.Sensitive, vstestOutput);
    }

    /// <summary>
    /// Where the rule cannot speak: CI and the command line run the tests through <c>test.sh</c>, whose
    /// <c>dotnet test --project</c> would stop at <c>MSB1001</c>. The script checks <c>global.json</c> first, naming it.
    /// </summary>
    [Theory]
    [InlineData("xunit")]
    [InlineData("nunit")]
    [InlineData("tunit")]
    public void The_test_script_fails_naming_global_json_before_dotnet_test_when_its_test_runner_is_missing_or_wrong(string framework)
    {
        using var project = new GeneratedProject("lean-crud", $"backend.test.framework={framework}");
        var globalJson = File.ReadAllText(Path.Combine(project.ProjectDirectory, "global.json"));
        string[] arguments = [".github/scripts/test.sh", ArchitectureTests];

        project.Write("global.json", globalJson[..globalJson.IndexOf(",\n  \"test\"", StringComparison.Ordinal)] + "\n}\n");
        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", arguments);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("global.json: test.runner is missing; it must be \"Microsoft.Testing.Platform\"", Case.Sensitive, output);
        output.ShouldNotContain("MSBUILD : error MSB1001", Case.Sensitive, output);

        project.Write("global.json", globalJson.Replace("Microsoft.Testing.Platform", "VSTest", StringComparison.Ordinal));
        var (vstestExitCode, vstestOutput) = Shell.Run(project.ProjectDirectory, "bash", arguments);

        vstestExitCode.ShouldNotBe(0, vstestOutput);
        vstestOutput.ShouldContain("global.json: test.runner is \"VSTest\", not \"Microsoft.Testing.Platform\"", Case.Sensitive, vstestOutput);
        vstestOutput.ShouldNotContain("MSBUILD : error MSB1001", Case.Sensitive, vstestOutput);
    }

    [Fact]
    public void The_check_passes_the_global_json_every_project_is_generated_with()
    {
        using var project = new GeneratedProject("lean-crud");

        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/check-test-runner.sh");

        exitCode.ShouldBe(0, output);
        output.ShouldBeEmpty();
    }

    /// <summary>The evals workflow runs <c>dotnet test --project</c> itself, not through <c>test.sh</c>, so it checks first too.</summary>
    [Fact]
    public void The_evals_workflow_checks_global_json_s_test_runner_before_it_runs_the_evals()
    {
        var evals = Files("saas-ai-react")[".github/workflows/evals.yml"];

        evals.ShouldContain("run: bash .github/scripts/check-test-runner.sh");
        evals.IndexOf("run: bash .github/scripts/check-test-runner.sh", StringComparison.Ordinal)
            .ShouldBeLessThan(evals.IndexOf("dotnet test --project", StringComparison.Ordinal));
    }

    private static (int ExitCode, string Output) RunArchitectureTests(GeneratedProject project) =>
        project.DotNetCommand(new Dictionary<string, string>(), "run", "--project", ArchitectureTests, "-c", "Release", "--no-build");

    /// <summary>The README's "Run locally" section, up to the next second-level heading.</summary>
    private static string RunLocally(string readme)
    {
        readme.ShouldContain("\n## Run locally\n");
        var section = readme[readme.IndexOf("\n## Run locally\n", StringComparison.Ordinal)..];
        var next = section.IndexOf("\n## ", 1, StringComparison.Ordinal);
        return next < 0 ? section : section[..next];
    }

    /// <summary>The project's files, generated from the real packs alone.</summary>
    private static Dictionary<string, string> Files(string profile)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"^(?<name>[a-z]+)@(?<major>\d+)\.")]
    private static partial Regex PackageManager();

    [GeneratedRegex(@"node-version: (\d+)")]
    private static partial Regex NodeVersion();
}

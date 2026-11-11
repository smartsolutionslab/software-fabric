using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The generator builds and runs on .NET 11 (#549), and the projects it generates pin the same SDK (#550), so a CI job
/// that builds a generated project sets up the generator's SDK and no other. A generated project's build still takes
/// the SDK its own global.json pins, not the one that runs the tests.
/// </summary>
public sealed class GeneratorSdkTests
{
    private static readonly string[] Workflows = ["ci.yml", "matrix.yml", "release.yml"];

    /// <summary>The scripts that build a generated project; build/matrix-generate.sh only generates.</summary>
    private static readonly string[] BuildingScripts = ["build/test-shard.sh", "build/profile-ci.sh", "build/matrix-combination.sh", "build/tool-check.sh"];

    [Fact]
    public void The_generator_builds_with_SDK_11_and_runs_on_dotnet_11()
    {
        var globalJson = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")))!;

        ((string?)globalJson["sdk"]?["version"]).ShouldBe("11.0.100");
        var targetFramework = typeof(GeneratorSdkTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        targetFramework.ShouldNotBeNull();
        targetFramework.FrameworkName.ShouldBe(".NETCoreApp,Version=v11.0");
        Environment.Version.Major.ShouldBe(11);
    }

    [Fact]
    public void Generated_projects_pin_the_generators_SDK()
    {
        var generator = (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")))!["sdk"]?["version"];
        var generated = (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "packs", "base", "core", "templates", "global.json.sbn")))!["sdk"]?["version"];

        generated.ShouldBe(generator);
    }

    [Fact]
    public void Every_job_that_builds_a_generated_project_sets_up_the_generators_SDK_alone()
    {
        var building = new List<string>();
        foreach (var workflow in Workflows)
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", workflow))));
            var jobs = (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode)["jobs"];
            foreach (var (key, value) in jobs.Children)
            {
                var steps = ((YamlSequenceNode)((YamlMappingNode)value)["steps"]).Children.Cast<YamlMappingNode>().ToList();
                var builds = steps.Any(step => Scalar(step, "run") is { } run && BuildingScripts.Any(script => run.Trim().StartsWith(script, StringComparison.Ordinal)));
                if (!builds)
                {
                    continue;
                }

                var job = $"{workflow} {((YamlScalarNode)key).Value}";
                building.Add(job);
                var setup = steps.SingleOrDefault(step => Scalar(step, "uses")?.StartsWith("actions/setup-dotnet@", StringComparison.Ordinal) == true);
                setup.ShouldNotBeNull($"{job} builds a generated project and sets up no SDK.");
                var with = (YamlMappingNode)setup["with"];
                Scalar(with, "global-json-file").ShouldBe("global.json", $"{job} takes the generator's SDK from global.json.");
                Scalar(with, "dotnet-version").ShouldBeNull($"{job} builds generated projects, which pin the generator's SDK; it needs no other.");
            }
        }

        building.ShouldBe(["ci.yml tests", "ci.yml profile", "matrix.yml combination", "release.yml release"], ignoreOrder: true);
    }

    /// <summary>
    /// <c>dotnet test</c> runs the tests with the generator's SDK in <c>MSBuildSDKsPath</c> and
    /// <c>MSBuildExtensionsPath</c>; a generated project's build, started by a test, must not inherit them, or SDK 10's
    /// MSBuild loads SDK 11's targets.
    /// </summary>
    [Fact]
    public void A_dotnet_command_a_test_runs_in_a_generated_project_uses_the_SDK_its_global_json_pins()
    {
        var template = Path.Combine(Repository.Root, "packs", "base", "core", "templates", "global.json.sbn");
        var pinned = (string?)JsonNode.Parse(File.ReadAllText(template))!["sdk"]?["version"];
        pinned.ShouldNotBeNull();
        var major = pinned.Split('.')[0];
        var project = Directory.CreateTempSubdirectory("generator-sdk-test-");
        try
        {
            File.Copy(template, Path.Combine(project.FullName, "global.json"));
            File.WriteAllText(
                Path.Combine(project.FullName, "Probe.csproj"),
                $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net{major}.0</TargetFramework></PropertyGroup></Project>""");

            var (exitCode, output) = DotNet.Run(project.FullName, new Dictionary<string, string>(), "msbuild", "Probe.csproj", "-getProperty:NETCoreSdkVersion");

            exitCode.ShouldBe(0, output);
            output.Trim().ShouldStartWith($"{major}.", Case.Sensitive, $"the project's global.json pins {pinned}; its build took the SDK of {output.Trim()}.");
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;
}

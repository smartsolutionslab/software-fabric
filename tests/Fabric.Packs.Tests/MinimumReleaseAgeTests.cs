using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// Every generated pnpm workspace installs only releases at least a day old (#876): <c>web/pnpm-workspace.yaml</c> sets
/// <c>minimumReleaseAge: 1440</c> with a comment saying why, a CI step fails when it is missing, and the agent rules take
/// a security fix sooner through <c>minimumReleaseAgeExclude</c>, never by removing the setting. Brand-new npm releases
/// the registry did not yet serve broke CI six times in three days (ERR_PNPM_NO_MATCHING_VERSION, ERR_PNPM_FETCH_404).
/// </summary>
public sealed class MinimumReleaseAgeTests
{
    private const string Dimension = "frontend.workspace";

    private const string WorkspaceFile = "web/pnpm-workspace.yaml";

    private const string Step = "The workspace installs only releases a day old";

    /// <summary>The packages each profile's workspace lists today; a profile not named here lists only the app.</summary>
    private static readonly Dictionary<string, string[]> Packages = new(StringComparer.Ordinal)
    {
        ["industrial-event-driven-mfe"] = ["app", "modules/*", "packages/tokens", "packages/ui"],
    };

    public static TheoryData<string> Workspaces => [.. Repository.Catalog.Dimensions[Dimension].Options.Keys];

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    [Fact]
    public void Every_workspace_option_of_the_catalog_writes_web_pnpm_workspace_yaml()
    {
        var options = Repository.Catalog.Dimensions[Dimension].Options.Keys.ToList();
        options.ShouldNotBeEmpty();

        var missing = options
            .Where(option => !Repository.Packs.Packs.TryGetValue($"{Dimension}.{option}", out var pack)
                || !pack.Files.Any(file => file.Target == WorkspaceFile))
            .ToList();

        missing.ShouldBeEmpty($"every {Dimension} pack writes {WorkspaceFile}");
    }

    [Theory]
    [MemberData(nameof(Workspaces))]
    public void Every_workspace_option_sets_a_minimum_release_age_of_a_day_with_the_reason(string workspace)
    {
        var content = Files("saas-standard-react", $"{Dimension}={workspace}")[WorkspaceFile];
        var root = Root(content);

        Scalar(root, "minimumReleaseAge").ShouldBe("1440", $"{WorkspaceFile} of {workspace}:\n{content}");
        PackageList(root).ShouldContain("app", $"{WorkspaceFile} of {workspace}:\n{content}");

        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var setting = Array.FindIndex(lines, line => line.StartsWith("minimumReleaseAge:", StringComparison.Ordinal));
        setting.ShouldBeGreaterThan(0, $"{WorkspaceFile} of {workspace} has a line above minimumReleaseAge:\n{content}");
        lines[setting - 1].ShouldStartWith("#", customMessage: $"the line directly above minimumReleaseAge is a comment saying why:\n{content}");

        var start = setting;
        while (start > 0 && lines[start - 1].StartsWith('#'))
        {
            start--;
        }

        string.Join('\n', lines[start..setting]).ShouldContain("minimumReleaseAgeExclude", Case.Sensitive, $"the comment names the way to take a fix sooner:\n{content}");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_profile_installs_only_releases_a_day_old(string profile)
    {
        var content = Files(profile)[WorkspaceFile];
        var root = Root(content);

        Scalar(root, "minimumReleaseAge").ShouldBe("1440", $"{WorkspaceFile} of {profile}:\n{content}");
        PackageList(root).ShouldBe(Packages.GetValueOrDefault(profile, ["app"]), ignoreOrder: false, customMessage: $"{WorkspaceFile} of {profile} lists the packages it listed before:\n{content}");
    }

    /// <summary>What a developer's pnpm sees: the generated workspace's setting, without installing anything.</summary>
    [Fact]
    public void Pnpm_reads_the_minimum_release_age_of_the_generated_workspace()
    {
        using var project = new GeneratedProject("saas-standard-react");
        var web = Path.Combine(project.ProjectDirectory, "web");

        var (exitCode, output) = Shell.Run(web, "pnpm", "config", "get", "minimumReleaseAge");

        exitCode.ShouldBe(0, $"pnpm config get minimumReleaseAge:\n{output}");
        output.Trim().ShouldBe("1440", $"pnpm config get minimumReleaseAge in web/:\n{output}");
    }

    /// <summary>The gate: the generated CI step passes on the generated workspace and fails once the setting is removed.</summary>
    [Fact]
    public void The_ci_gate_fails_when_the_workspace_sets_no_minimum_release_age()
    {
        using var project = new GeneratedProject("saas-standard-react");
        var (script, workingDirectory) = CiStep(project.ProjectDirectory);

        var (passingExitCode, passingOutput) = Shell.Run(workingDirectory, "bash", "--noprofile", "--norc", "-eo", "pipefail", "-c", script);
        passingExitCode.ShouldBe(0, $"\"{Step}\" on the generated workspace:\n{passingOutput}");

        var path = Path.Combine(project.ProjectDirectory, "web", "pnpm-workspace.yaml");
        var without = File.ReadAllLines(path).Where(line => !line.StartsWith("minimumReleaseAge:", StringComparison.Ordinal));
        File.WriteAllText(path, string.Join('\n', without) + "\n");

        var (failingExitCode, failingOutput) = Shell.Run(workingDirectory, "bash", "--noprofile", "--norc", "-eo", "pipefail", "-c", script);
        failingExitCode.ShouldNotBe(0, $"\"{Step}\" without minimumReleaseAge:\n{failingOutput}");
        failingOutput.ShouldContain("sets no minimumReleaseAge", customMessage: failingOutput);
    }

    [Theory]
    [MemberData(nameof(Workspaces))]
    public void The_workspace_agent_rules_take_a_security_fix_through_minimumReleaseAgeExclude_never_by_removing_the_setting(string workspace)
    {
        var agents = Files("saas-standard-react", $"{Dimension}={workspace}")["AGENTS.md"];

        foreach (var phrase in new[] { "minimumReleaseAge: 1440", "minimumReleaseAgeExclude", "the reason in the pull request", "Never remove or lower it" })
        {
            agents.ShouldContain(phrase, Case.Sensitive, $"AGENTS.md of {workspace} says \"{phrase}\"");
        }
    }

    /// <summary>The run script and working directory of the CI step that checks the setting, from the generated ci.yml.</summary>
    private static (string Script, string WorkingDirectory) CiStep(string projectDirectory)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(projectDirectory, ".github", "workflows", "ci.yml"))));
        var jobs = ((YamlMappingNode)stream.Documents[0].RootNode).Children[new YamlScalarNode("jobs")].ShouldBeOfType<YamlMappingNode>();
        var step = jobs.Children.Values
            .OfType<YamlMappingNode>()
            .SelectMany(job => job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps) ? ((YamlSequenceNode)steps).Children : [])
            .OfType<YamlMappingNode>()
            .FirstOrDefault(candidate => Scalar(candidate, "name") == Step);
        step.ShouldNotBeNull($"the generated ci.yml has a step named \"{Step}\"");

        var script = Scalar(step, "run");
        script.ShouldNotBeNull($"\"{Step}\" runs a script");
        var workingDirectory = Scalar(step, "working-directory");
        workingDirectory.ShouldBe("web", $"\"{Step}\" runs in web/");
        return (script, Path.Combine(projectDirectory, "web"));
    }

    private static YamlMappingNode Root(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        return stream.Documents[0].RootNode.ShouldBeOfType<YamlMappingNode>();
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? (value as YamlScalarNode)?.Value : null;

    private static List<string> PackageList(YamlMappingNode root) =>
        [.. root.Children[new YamlScalarNode("packages")].ShouldBeOfType<YamlSequenceNode>().Children.Select(item => ((YamlScalarNode)item).Value!)];

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty(string.Join(' ', overrides));
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

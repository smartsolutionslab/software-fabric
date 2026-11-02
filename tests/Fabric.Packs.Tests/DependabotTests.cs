using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// A generated project's <c>.github/dependabot.yml</c> keeps every package ecosystem it has current, and only those:
/// NuGet for <c>Directory.Packages.props</c>, npm for the frontend workspace when one is generated, and the actions
/// the workflows pin. Minor and patch updates come grouped; a major one comes on its own.
/// </summary>
public sealed partial class DependabotTests
{
    private const string ConfigPath = ".github/dependabot.yml";

    private static readonly string[] MinorAndPatch = ["minor", "patch"];

    private static readonly string[] CommitTypes = ["build", "chore", "ci", "docs", "feat", "fix", "perf", "refactor", "revert", "style", "test"];

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    [Fact]
    public void A_full_stack_project_keeps_nuget_its_pnpm_workspace_and_its_actions_current()
    {
        using var project = new GeneratedProject("saas-standard-react");

        Entries(project.ProjectDirectory).ShouldBe(["github-actions /", "npm /web", "nuget /"]);
    }

    /// <summary>
    /// The catalog has no profile without a frontend yet (<c>frontend.framework</c> has no <c>none</c>), so a backend-only
    /// project is the lean-crud backend with an empty pack in place of every frontend option it chooses.
    /// </summary>
    [Fact]
    public void A_backend_only_project_keeps_nuget_and_its_actions_current_and_has_no_npm_entry()
    {
        var frontend = FrontendOptions("lean-crud").Select(option => new ProbePack(option, new Dictionary<string, string>())).ToList();
        try
        {
            using var project = new GeneratedProject("lean-crud", [.. frontend.Select(pack => pack.Manifest)]);

            File.Exists(Path.Combine(project.ProjectDirectory, "web", "package.json")).ShouldBeFalse();
            Entries(project.ProjectDirectory).ShouldBe(["github-actions /", "nuget /"]);
        }
        finally
        {
            frontend.ForEach(pack => pack.Dispose());
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_ecosystem_a_generated_project_has_gets_one_entry_and_none_it_lacks_does(string profile)
    {
        using var project = new GeneratedProject(profile, FormattingTests.FrontendStandIns.GetValueOrDefault(profile, []));

        Entries(project.ProjectDirectory).ShouldBe(Present(project.ProjectDirectory));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_entry_groups_minor_and_patch_updates_and_never_major_ones(string profile)
    {
        using var project = new GeneratedProject(profile, FormattingTests.FrontendStandIns.GetValueOrDefault(profile, []));

        foreach (var entry in Updates(project.ProjectDirectory))
        {
            var name = $"{Scalar(entry, "package-ecosystem")} {Scalar(entry, "directory")}";
            var groups = Value(entry, "groups").ShouldBeOfType<YamlMappingNode>(name);
            var updateTypes = groups.Children.Values
                .Select(group => Value(group, "update-types").ShouldBeOfType<YamlSequenceNode>(name))
                .Select(types => types.Children.Select(type => type.ToString()).Order(StringComparer.Ordinal).ToList())
                .ToList();

            updateTypes.ShouldContain(types => types.SequenceEqual(MinorAndPatch), name);
            updateTypes.ShouldAllBe(types => !types.Contains("major"), name);
        }
    }

    /// <summary>The project's commit check refuses anything else, so Dependabot's commits name a type and a known scope.</summary>
    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_entry_commits_as_a_conventional_commit_the_project_s_commit_check_accepts(string profile)
    {
        using var project = new GeneratedProject(profile, FormattingTests.FrontendStandIns.GetValueOrDefault(profile, []));
        var scopes = File.ReadAllLines(Path.Combine(project.ProjectDirectory, ".github", "commit-scopes"))
            .Where(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Trim())
            .ToList();

        foreach (var entry in Updates(project.ProjectDirectory))
        {
            var name = $"{Scalar(entry, "package-ecosystem")} {Scalar(entry, "directory")}";
            var prefix = Scalar(Value(entry, "commit-message").ShouldBeOfType<YamlMappingNode>(name), "prefix").ShouldNotBeNull(name);

            var header = ConventionalPrefix().Match(prefix);
            header.Success.ShouldBeTrue($"{name}: '{prefix}' is not <type>(<scope>)");
            header.Groups["type"].Value.ShouldBeOneOf(CommitTypes, name);
            scopes.ShouldContain(header.Groups["scope"].Value, name);
        }
    }

    /// <summary>
    /// The owner's decision on #55: red-first lets a pull request by dependabot[bot] that changes dependency manifests
    /// and lockfiles only pass with a note, though it changes web/ and quotes no failing tests; anything else it holds
    /// to the rule. The workflow's step runs as Actions would run it, against a pull request of one commit.
    /// </summary>
    [Theory]
    [InlineData("dependabot[bot]", false, 0)]
    [InlineData("dependabot[bot]", true, 1)]
    [InlineData("developer", false, 1)]
    public void Red_first_passes_a_dependabot_update_of_manifests_only_with_a_note_and_holds_everything_else(string author, bool alsoSource, int expectedExitCode)
    {
        using var project = new GeneratedProject("saas-standard-react");
        var change = CrossModelReviewTests.DependencyUpdate.ToDictionary();
        if (alsoSource)
        {
            change["src/Kernel/Paid.cs"] = ("namespace Shop.Kernel;\n", "namespace Shop.Kernel;\n\npublic sealed record Paid;\n");
        }

        var directory = project.ProjectDirectory;
        foreach (var (file, (before, _)) in change)
        {
            project.Write(file, before);
        }

        BoardProject.Git(directory, "init", "--quiet", "--initial-branch=main");
        BoardProject.Git(directory, "add", "--all");
        BoardProject.Git(directory, "commit", "--quiet", "-m", "base");
        var baseSha = BoardProject.Git(directory, "rev-parse", "HEAD");
        foreach (var (file, (_, after)) in change)
        {
            project.Write(file, after);
        }

        BoardProject.Git(directory, "add", "--all");
        BoardProject.Git(directory, "commit", "--quiet", "-m", "chore(deps): Bump the dependencies");

        var (exitCode, output) = RunRedFirst(directory, new Dictionary<string, string>
        {
            ["github.event.pull_request.body"] = "Bumps the dependencies.",
            ["github.event.pull_request.base.sha"] = baseSha,
            ["github.event.pull_request.user.login"] = author,
        });

        exitCode.ShouldBe(expectedExitCode, output);
        if (expectedExitCode == 0)
        {
            output.ShouldContain("Dependency update by Dependabot: not reviewed by a model; build, tests and the other gates apply", Case.Sensitive, output);
        }
    }

    /// <summary>Runs every <c>run</c> step of red-first.yml in the checkout, with the given expressions; a checkout step is the directory itself.</summary>
    private static (int ExitCode, string Output) RunRedFirst(string directory, Dictionary<string, string> expressions)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(directory, ".github", "workflows", "red-first.yml"))));
        var jobs = Value(stream.Documents[0].RootNode, "jobs").ShouldBeOfType<YamlMappingNode>();
        var runnerTemp = Directory.CreateTempSubdirectory("fabric-runner-").FullName;
        try
        {
            var output = "";
            foreach (var step in jobs.Children.Values.SelectMany(job => Value(job, "steps").ShouldBeOfType<YamlSequenceNode>().Children))
            {
                if (Scalar(step, "run") is not { } script)
                {
                    continue;
                }

                Dictionary<string, string> environment = new(BoardProject.GitIdentity) { ["RUNNER_TEMP"] = runnerTemp };
                if (Value(step, "env") is YamlMappingNode env)
                {
                    foreach (var (key, value) in env.Children)
                    {
                        environment[((YamlScalarNode)key).Value!] = Expression().Replace(((YamlScalarNode)value).Value!, match =>
                        {
                            expressions.ShouldContainKey(match.Groups[1].Value, $"red-first.yml uses ${{{{ {match.Groups[1].Value} }}}}, which this test does not provide.");
                            return expressions[match.Groups[1].Value];
                        });
                    }
                }

                var (exitCode, stepOutput) = Shell.Run(directory, environment, "bash", "--noprofile", "--norc", "-eo", "pipefail", "-c", script);
                output += stepOutput;
                if (exitCode != 0)
                {
                    return (exitCode, output);
                }
            }

            return (0, output);
        }
        finally
        {
            Directory.Delete(runnerTemp, recursive: true);
        }
    }

    /// <summary>Every option of a frontend dimension the profile chooses.</summary>
    private static IEnumerable<string> FrontendOptions(string profile)
    {
        var choices = Core.Choices.ChoiceResolver.Resolve(Repository.Catalog, profile, []).Choices.ShouldNotBeNull();
        return choices.Project
            .Where(entry => entry.Key.StartsWith("frontend.", StringComparison.Ordinal))
            .SelectMany(entry => entry.Value.Select(option => $"{entry.Key}.{option}"));
    }

    /// <summary>"&lt;ecosystem&gt; &lt;directory&gt;" for every update in the project's Dependabot configuration, sorted.</summary>
    private static List<string> Entries(string root) =>
        [.. Updates(root).Select(entry => $"{Scalar(entry, "package-ecosystem")} {Scalar(entry, "directory")}").Order(StringComparer.Ordinal)];

    /// <summary>
    /// "&lt;ecosystem&gt; &lt;directory&gt;" for every ecosystem the project's files show, sorted: NuGet where central
    /// package versions are, npm at every workspace root (a package.json with none above it), actions for the workflows.
    /// </summary>
    private static List<string> Present(string root)
    {
        List<string> present = [];
        if (File.Exists(Path.Combine(root, "Directory.Packages.props")))
        {
            present.Add("nuget /");
        }

        if (Directory.Exists(Path.Combine(root, ".github", "workflows")) && Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml").Any())
        {
            present.Add("github-actions /");
        }

        var packages = Directory.EnumerateFiles(root, "package.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetDirectoryName(path)!)
            .ToList();
        present.AddRange(packages
            .Where(directory => !packages.Any(other => other != directory && directory.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .Select(directory => $"npm /{Path.GetRelativePath(root, directory).Replace('\\', '/')}"));

        return [.. present.Order(StringComparer.Ordinal)];
    }

    private static List<YamlMappingNode> Updates(string root)
    {
        var path = Path.Combine(root, ConfigPath);
        File.Exists(path).ShouldBeTrue($"{ConfigPath} was not generated");

        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        var config = stream.Documents[0].RootNode.ShouldBeOfType<YamlMappingNode>();
        Scalar(config, "version").ShouldBe("2");

        return [.. Value(config, "updates").ShouldBeOfType<YamlSequenceNode>().Children.Select(entry => entry.ShouldBeOfType<YamlMappingNode>())];
    }

    private static YamlNode? Value(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Scalar(YamlNode? node, string key) => (Value(node, key) as YamlScalarNode)?.Value;

    [GeneratedRegex(@"^(?<type>[a-z]+)\((?<scope>[a-z0-9-]+)\)$")]
    private static partial Regex ConventionalPrefix();

    [GeneratedRegex(@"\$\{\{\s*(.+?)\s*\}\}")]
    private static partial Regex Expression();
}

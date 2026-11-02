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
}

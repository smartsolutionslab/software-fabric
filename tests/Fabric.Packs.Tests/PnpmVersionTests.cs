using System.Text.RegularExpressions;
using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The pnpm version of generated projects is stated once, as the base pack's <c>pnpm_version</c> template function, and
/// every workspace pack and this repository's CI use it (#877). It is at least 10.16, the first pnpm that reads
/// <c>minimumReleaseAge</c> (#876).
/// </summary>
public sealed partial class PnpmVersionTests
{
    private const string Functions = "packs/base/core/templates/functions.sbn";

    public static TheoryData<string> Workspaces => ["pnpm-workspaces", "nx", "turborepo"];

    [Fact]
    public void The_base_pack_states_the_pnpm_version_once_at_10_16_or_later()
    {
        var version = Version();

        System.Version.Parse(version).ShouldBeGreaterThanOrEqualTo(new System.Version(10, 16, 0));
    }

    [Theory]
    [MemberData(nameof(Workspaces))]
    public void Every_workspace_pack_pins_the_stated_pnpm_version(string workspace)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "saas-standard-react", [$"frontend.workspace={workspace}"]);
        generated.Errors.ShouldBeEmpty();

        generated.Files.Single(file => file.Path == "web/package.json").Content
            .ShouldContain($"\"packageManager\": \"pnpm@{Version()}\"");
    }

    [Fact]
    public void No_pack_template_names_a_pnpm_version_of_its_own()
    {
        var named = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*.sbn", SearchOption.AllDirectories)
            .Where(path => PinnedPnpm().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        named.ShouldBeEmpty();
    }

    [Fact]
    public void This_repositorys_workflows_set_up_the_stated_pnpm_version()
    {
        List<string> setups = [];
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Repository.Root, ".github"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("pnpm/action-setup@", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            var workflow = new YamlStream();
            workflow.Load(new StringReader(File.ReadAllText(path)));
            var jobs = (YamlMappingNode)((YamlMappingNode)workflow.Documents[0].RootNode).Children[new YamlScalarNode("jobs")];
            foreach (var step in jobs.Children.Values.OfType<YamlMappingNode>()
                .SelectMany(job => job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps) ? ((YamlSequenceNode)steps).Children : [])
                .OfType<YamlMappingNode>()
                .Where(step => step.Children.TryGetValue(new YamlScalarNode("uses"), out var uses) && ((YamlScalarNode)uses).Value!.StartsWith("pnpm/action-setup@", StringComparison.Ordinal)))
            {
                var version = step.Children.TryGetValue(new YamlScalarNode("with"), out var with)
                    && ((YamlMappingNode)with).Children.TryGetValue(new YamlScalarNode("version"), out var value)
                    ? ((YamlScalarNode)value).Value
                    : null;
                setups.Add($"{Path.GetFileName(path)}: {version}");
            }
        }

        setups.ShouldNotBeEmpty();
        setups.ShouldAllBe(setup => setup.EndsWith($": {Version()}", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_base_pack_defines_pnpm_version()
    {
        // templates.functions contributions are joined in pack order and the last definition wins, so a second one would
        // silently override the base's.
        var defining = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*.sbn", SearchOption.AllDirectories)
            .Where(path => Definition().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .ToList();

        defining.ShouldBe([Functions]);
    }

    [Fact]
    public void No_pack_sets_up_a_pnpm_version_of_its_own()
    {
        // A pack's pnpm/action-setup step reads the version from packageManager; a version: on it would override that.
        var setting = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*.sbn", SearchOption.AllDirectories)
            .Where(path => ActionSetupVersion().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        setting.ShouldBeEmpty();
    }

    [Fact]
    public void No_pack_names_a_pnpm_major_in_prose()
    {
        var named = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*.sbn", SearchOption.AllDirectories)
            .Where(path => PnpmMajor().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        named.ShouldBeEmpty();
    }

    /// <summary>The version the base pack's <c>pnpm_version</c> function returns.</summary>
    private static string Version()
    {
        var path = Path.Combine(Repository.Root, Functions);
        File.Exists(path).ShouldBeTrue($"{Functions} states the pnpm version");
        var match = StatedVersion().Match(File.ReadAllText(path));
        match.Success.ShouldBeTrue($"{Functions} defines func pnpm_version returning a version");
        return match.Groups["version"].Value;
    }

    [GeneratedRegex(@"func pnpm_version\s+ret ""(?<version>\d+\.\d+\.\d+)""")]
    private static partial Regex StatedVersion();

    [GeneratedRegex(@"pnpm@\d")]
    private static partial Regex PinnedPnpm();

    [GeneratedRegex(@"func pnpm_version\b")]
    private static partial Regex Definition();

    [GeneratedRegex(@"pnpm/action-setup@[^\n]*\n(?:(?>[ \t]+)(?!- |version:)[^\n]*\n)*(?>[ \t]+)version:", RegexOptions.None)]
    private static partial Regex ActionSetupVersion();

    [GeneratedRegex(@"\bpnpm \d+\b")]
    private static partial Regex PnpmMajor();
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// Generated workflows and this repository's own run actions pinned to a commit, since a tag can be moved to code
/// nobody reviewed, and one run per workflow and ref, so a push does not leave the runs before it queued up.
/// </summary>
public sealed partial class WorkflowTests
{
    /// <summary>They write to issues and the board; a cancelled or superseded run would lose a write.</summary>
    private static readonly string[] BoardWriters = ["board-janitor.yml", "board-return.yml"];

    private const string Group = "${{ github.workflow }}-${{ github.ref }}";

    /// <summary>A workflow naming actions in every way YAML allows; only line 14 is pinned as the checks require.</summary>
    private const string Spellings = """
        name: spellings
        on: push
        jobs:
          flow:
            runs-on: ubuntu-latest
            steps:
              - { uses: actions/checkout@v4 }
              - uses: "actions/setup-node@v4"
              - "uses": actions/setup-dotnet@v4
              - name: folded
                uses: >-
                  pnpm/action-setup@v4
              - { name: pinned-in-flow, uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 }
              - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0
              - run: echo "a step that only mentions uses in its text"
              - {"uses":"actions/checkout@v4"}
        """;

    private static readonly int[] SpellingsUnpinnedLines = [7, 8, 9, 11, 13, 16];

    /// <summary>The profiles the real packs generate alone, which profile CI builds (ADR-0003).</summary>
    public static TheoryData<string> Profiles => new(
        Repository.Catalog.Profiles.Keys.Where(profile => Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []).Errors.Count == 0));

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_action_a_generated_workflow_uses_is_pinned_to_a_commit(string profile)
    {
        using var project = new GeneratedProject(profile);

        Unpinned(project.ProjectDirectory, Workflows(project.ProjectDirectory)).ShouldBeEmpty();
    }

    /// <summary>Also the packs no complete profile chooses yet: any file a pack writes workflow steps from.</summary>
    [Fact]
    public void Every_action_a_pack_template_uses_is_pinned_to_a_commit()
    {
        var packs = Path.Combine(Repository.Root, "packs");

        Unpinned(packs, Directory.EnumerateFiles(packs, "*", SearchOption.AllDirectories).Order()).ShouldBeEmpty();
    }

    [Fact]
    public void A_generated_project_s_CI_checks_that_its_actions_are_pinned_and_a_fresh_project_passes()
    {
        using var project = new GeneratedProject("lean-crud");
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));

        ci.ShouldContain("run: bash .github/scripts/check-actions-pinned.sh");
        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/check-actions-pinned.sh");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void An_action_used_by_tag_or_without_its_version_fails_the_generated_check()
    {
        using var project = new GeneratedProject("lean-crud");
        var ciPath = Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml");
        var ci = File.ReadAllText(ciPath);
        var pinned = Uses().Match(ci.Split('\n').First(line => line.Contains("actions/checkout@", StringComparison.Ordinal))).Groups["action"].Value;
        File.WriteAllText(ciPath, ci.Replace(pinned, "actions/checkout@v4", StringComparison.Ordinal));
        project.Write(".github/workflows/extra.yml", $"""
            name: extra
            on: push
            jobs:
              extra:
                runs-on: ubuntu-latest
                steps:
                  - uses: {pinned}
                  - uses: ./.github/actions/local
            """);

        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/check-actions-pinned.sh");

        exitCode.ShouldBe(1, output);
        output.ShouldContain("file=.github/workflows/ci.yml,line=", Case.Sensitive, output);
        output.ShouldContain("actions/checkout@v4", Case.Sensitive, output);
        output.ShouldContain("file=.github/workflows/extra.yml,line=7::", Case.Sensitive, output);
        output.ShouldNotContain("./.github/actions/local", Case.Sensitive, output);
    }

    [Fact]
    public void An_action_spelled_as_a_flow_mapping_quoted_or_on_its_own_line_fails_the_generated_check()
    {
        using var project = new GeneratedProject("lean-crud");
        project.Write(".github/workflows/spellings.yml", Spellings);

        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/check-actions-pinned.sh");

        exitCode.ShouldBe(1, output);
        foreach (var line in SpellingsUnpinnedLines)
        {
            output.ShouldContain($"file=.github/workflows/spellings.yml,line={line}::", Case.Sensitive, output);
        }

        output.Split('\n').Count(text => text.Contains("file=.github/workflows/spellings.yml,", StringComparison.Ordinal))
            .ShouldBe(SpellingsUnpinnedLines.Length, output);
    }

    [Fact]
    public void The_generator_s_own_check_finds_an_action_however_the_workflow_spells_it()
    {
        var root = Directory.CreateTempSubdirectory("fabric-spellings-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".github", "workflows"));
            File.WriteAllText(Path.Combine(root, ".github", "workflows", "spellings.yml"), Spellings);

            File.WriteAllText(Path.Combine(root, ".github", "workflows", "extra.yaml"), "jobs:\n  a:\n    steps:\n      - uses: actions/checkout@v4\n");

            Unpinned(root, Workflows(root)).Select(entry => entry[..entry.IndexOf(": ", StringComparison.Ordinal)])
                .ShouldBe([".github/workflows/extra.yaml:4", .. SpellingsUnpinnedLines.Select(line => $".github/workflows/spellings.yml:{line}")]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Every_action_this_repository_s_workflows_use_is_pinned_to_a_commit() =>
        Unpinned(Repository.Root, Workflows(Repository.Root)).ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_generated_workflow_runs_once_per_ref_and_cancels_only_where_a_newer_run_supersedes_it(string profile)
    {
        using var project = new GeneratedProject(profile);

        Misgrouped(project.ProjectDirectory).ShouldBeEmpty();
    }

    [Fact]
    public void This_repository_s_workflows_run_once_per_ref_and_cancel_only_where_a_newer_run_supersedes_it() =>
        Misgrouped(Repository.Root).ShouldBeEmpty();

    private static IEnumerable<string> Workflows(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml").Order();

    /// <summary>
    /// Every line with a <c>uses</c> key that is not a local action or <c>uses: action@&lt;40 hex&gt; # v…</c>. It
    /// reads lines, not YAML (templates are not YAML yet), so any other spelling fails closed, as the generated check does.
    /// </summary>
    private static List<string> Unpinned(string root, IEnumerable<string> files) =>
    [
        .. files.SelectMany(path => File.ReadLines(path)
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => UsesKey().IsMatch(entry.Line) && !Pinned().IsMatch(entry.Line) && !LocalAction().IsMatch(entry.Line))
            .Select(entry => $"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{entry.Number}: {entry.Line.Trim()}")),
    ];

    /// <summary>Every workflow whose top-level <c>concurrency</c> differs from what its triggers call for.</summary>
    private static List<string> Misgrouped(string root)
    {
        List<string> problems = [];
        foreach (var path in Workflows(root))
        {
            var name = Path.GetFileName(path);
            var stream = new YamlStream();
            stream.Load(new StringReader(File.ReadAllText(path)));
            var workflow = (YamlMappingNode)stream.Documents[0].RootNode;
            workflow.Children.TryGetValue(new YamlScalarNode("concurrency"), out var concurrency);

            if (BoardWriters.Contains(name))
            {
                if (concurrency is not null)
                {
                    problems.Add($"{name}: writes to the board and must not share a concurrency group");
                }

                continue;
            }

            if (concurrency is not YamlMappingNode settings)
            {
                problems.Add($"{name}: has no concurrency group");
                continue;
            }

            var triggers = workflow.Children[new YamlScalarNode("on")] is YamlMappingNode events
                ? events.Children.Keys.Select(key => key.ToString()).ToList()
                : [];
            var cancel = triggers.SequenceEqual(["pull_request"]) ? "true" : "${{ github.event_name == 'pull_request' }}";

            if (Scalar(settings, "cancel-in-progress") != cancel)
            {
                problems.Add($"{name}: cancel-in-progress is '{Scalar(settings, "cancel-in-progress")}', expected '{cancel}'");
            }

            if (Scalar(settings, "group") != Group)
            {
                problems.Add($"{name}: group is '{Scalar(settings, "group")}', expected '{Group}'");
            }
        }

        return problems;
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;

    [GeneratedRegex(@"^\s*(?:-\s+)?uses:\s*(?<action>\S+)(?<rest>.*)$")]
    private static partial Regex Uses();

    /// <summary>A <c>uses</c> key anywhere YAML allows one: a block or flow mapping, quoted or not.</summary>
    [GeneratedRegex(@"(^|[\s{,])[""']?uses[""']?\s*:(\s|$)")]
    private static partial Regex UsesKey();

    [GeneratedRegex(@"^\s*(-\s+)?uses:\s*[^@\s]+@[0-9a-f]{40}\s+#\s*v\d\S*\s*$")]
    private static partial Regex Pinned();

    [GeneratedRegex(@"^\s*(-\s+)?uses:\s*\./")]
    private static partial Regex LocalAction();
}

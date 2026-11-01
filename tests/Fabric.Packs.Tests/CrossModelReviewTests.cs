using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The generated cross-model review workflow, run step by step as Actions would run it, with stand-ins for the
/// Copilot CLI, npm and gh: the reviewer is GitHub Copilot CLI, on the model of the family that wrote none of the
/// commits, and only findings in the schema's shape pass.
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
    public void Copilot_signs_in_with_the_copilot_secret_and_without_gh_token()
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings);

        run.ExitCode.ShouldBe(0, run.Log);
        File.ReadAllText(Path.Combine(Fakes, "copilot-env")).ShouldBe("COPILOT_GITHUB_TOKEN=personal-token\nGH_TOKEN=unset\nGITHUB_TOKEN=unset\n");
    }

    /// <summary>Actions gives a secret the repository does not have as an empty string.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_the_copilot_secret_the_review_fails_naming_it_and_copilot_does_not_run(string secret)
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", NoFindings, secret: secret);

        run.ExitCode.ShouldNotBe(0, run.Log);
        run.Log.ShouldContain("COPILOT_GITHUB_TOKEN");
        run.Log.ShouldContain("Copilot Requests");
        run.CopilotArguments.ShouldBeNull(run.Log);
        Reviews().ShouldBeEmpty();
    }

    [Fact]
    public void The_review_job_uses_the_copilot_secret_alone_and_no_model_vendor_s_key()
    {
        var workflow = File.ReadAllText(WorkflowPath);
        var jobs = (YamlMappingNode)Root()["jobs"];

        jobs.Children.Keys.Select(key => ((YamlScalarNode)key).Value).ShouldBe(["choose", "review"]);
        var permissions = (YamlMappingNode)((YamlMappingNode)jobs["review"])["permissions"];
        permissions.Children.Keys.Select(key => ((YamlScalarNode)key).Value).ShouldNotContain("copilot-requests");
        workflow.ShouldNotContain("ANTHROPIC_API_KEY");
        workflow.ShouldNotContain("OPENAI_API_KEY");
        SecretReference().Matches(workflow).Select(match => match.Groups[1].Value).Distinct().ShouldBe(["COPILOT_GITHUB_TOKEN"]);
    }

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

    [Theory]
    [InlineData("I reviewed the change; it looks fine.", 0)]
    [InlineData("```json\n{\"findings\": []}\n```", 0)]
    [InlineData("""{"findings": [{"severity": "blocking", "file": "src/Invoice.cs", "line": 12, "message": "Paying twice charges twice."}]}""", 0)]
    [InlineData(NoFindings, 1)]
    public void A_review_fails_unless_copilot_answers_in_the_findings_shape_with_nothing_blocking(string answer, int copilotExitCode)
    {
        var run = Review("Change\n\nAgent-Executor: claude-cloud/lane-backend", answer, copilotExitCode);

        run.ExitCode.ShouldNotBe(0, run.Log);
        run.CopilotArguments.ShouldNotBeNull(run.Log);
    }

    private YamlMappingNode Root()
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(WorkflowPath);
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

    private sealed record ReviewRun(int ExitCode, string Log, List<string>? CopilotArguments);

    /// <summary>
    /// A pull request of one commit, reviewed: the choose job, then the review job, with the copilot stand-in answering
    /// <paramref name="answer"/> and exiting with <paramref name="copilotExitCode"/>. The gate's files are the generated
    /// project's, as the base branch's checkout in .gate.
    /// </summary>
    private ReviewRun Review(string commitMessage, string answer, int copilotExitCode = 0, string secret = "personal-token")
    {
        var workspace = Path.Combine(state, "workspace");
        Directory.CreateDirectory(workspace);
        BoardProject.Git(workspace, "init", "--quiet", "--initial-branch=main");
        BoardProject.Git(workspace, "commit", "--quiet", "--allow-empty", "-m", "base");
        BoardProject.Git(workspace, "tag", "base");
        BoardProject.Git(workspace, "commit", "--quiet", "--allow-empty", "-m", commitMessage);
        Directory.CreateSymbolicLink(Path.Combine(workspace, ".gate"), board.Project.ProjectDirectory);
        WriteFakes(answer, copilotExitCode);

        Dictionary<string, string> expressions = new()
        {
            ["github.event.pull_request.base.sha"] = "base",
            ["github.event.pull_request.head.sha"] = "HEAD",
            ["github.event.pull_request.number"] = "5",
            ["github.token"] = "workflow-token",
            ["secrets.COPILOT_GITHUB_TOKEN"] = secret,
        };
        var choose = RunJob("choose", workspace, expressions);
        if (choose.ExitCode != 0)
        {
            return new ReviewRun(choose.ExitCode, choose.Log, null);
        }

        foreach (var (name, value) in choose.Outputs)
        {
            expressions[$"needs.choose.outputs.{name}"] = value;
        }

        var review = RunJob("review", workspace, expressions);
        var arguments = Path.Combine(Fakes, "copilot-args");
        List<string>? copilotArguments = File.Exists(arguments) ? [.. File.ReadAllText(arguments).Split('\0')[..^1]] : null;
        return new ReviewRun(review.ExitCode, choose.Log + review.Log, copilotArguments);
    }

    private void WriteFakes(string answer, int copilotExitCode)
    {
        Directory.CreateDirectory(Fakes);
        File.WriteAllText(Path.Combine(Fakes, "answer"), answer);
        Fake("copilot", $$"""
            #!/usr/bin/env bash
            printf '%s\0' "$@" > "$FAKES/copilot-args"
            printf 'COPILOT_GITHUB_TOKEN=%s\nGH_TOKEN=%s\nGITHUB_TOKEN=%s\n' "${COPILOT_GITHUB_TOKEN-unset}" "${GH_TOKEN-unset}" "${GITHUB_TOKEN-unset}" > "$FAKES/copilot-env"
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
    /// over); its <c>uses</c> steps are checkouts, which the workspace stands in for. An expression the test does not
    /// know, a secret say, fails the test.
    /// </summary>
    private JobRun RunJob(string name, string workspace, Dictionary<string, string> expressions)
    {
        var job = (YamlMappingNode)((YamlMappingNode)Root()["jobs"])[name];
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
        foreach (var (key, value) in WorkflowEnvironment().Concat(Mapping(job, "env")))
        {
            environment[key] = Evaluate(value, expressions, environment, []);
        }

        Dictionary<string, Dictionary<string, string>> steps = [];
        var log = "";
        foreach (var step in ((YamlSequenceNode)job["steps"]).Cast<YamlMappingNode>())
        {
            if (!step.Children.TryGetValue(new YamlScalarNode("run"), out var run))
            {
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

    [GeneratedRegex(@"^@github/copilot@\d+\.\d+\.\d+$")]
    private static partial Regex ExactCopilotVersion();
}

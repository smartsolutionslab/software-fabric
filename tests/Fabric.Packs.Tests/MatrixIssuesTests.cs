using System.Text.Json.Nodes;

namespace Fabric.Packs.Tests;

/// <summary>
/// ADR-0003: a <c>supported</c> option whose nightly run fails becomes a <c>bug</c> issue. build/matrix-issues.sh
/// opens one per failing combination that holds a supported option, marked with the combination's fingerprint, and
/// on a rerun comments on the open issue instead of opening a second (roadmap M3-04).
/// </summary>
public sealed class MatrixIssuesTests : IDisposable
{
    /// <summary>A stand-in for the GitHub CLI holding issues in $GH_FAKE_STATE; it answers the calls matrix-issues.sh makes and fails on any other.</summary>
    private const string FakeGh = """
        #!/usr/bin/env bash
        set -euo pipefail
        state="$GH_FAKE_STATE"
        echo "gh $*" >> "$state/log"
        method=GET paginate=""
        fields=()
        positional=()
        while [ $# -gt 0 ]; do
          case "$1" in
            -X) method="$2"; shift 2 ;;
            -f) fields+=("$2"); shift 2 ;;
            --paginate) paginate=1; shift ;;
            *) positional+=("$1"); shift ;;
          esac
        done
        [ "${positional[0]}" = api ] || { echo "fake gh: unexpected call: ${positional[*]}" >&2; exit 1; }
        field() { local entry; for entry in "${fields[@]}"; do [ "${entry%%=*}" = "$1" ] && printf '%s' "${entry#*=}"; done; return 0; }
        issues="$state/issues.json"
        path="${positional[1]}"
        case "$method $path" in
          "GET repos/acme/shop/issues?state=open&labels=bug&per_page=100")
            [ -n "$paginate" ] || { echo "fake gh: list without --paginate" >&2; exit 1; }
            jq -c 'map(select(.state == "open" and ([.labels[].name] | index("bug"))))' "$issues"
            ;;
          "POST repos/acme/shop/issues")
            number="$(jq 'length + 1' "$issues")"
            jq --argjson number "$number" --arg title "$(field title)" --arg body "$(field body)" --arg label "$(field 'labels[]')" \
              '. + [{number: $number, title: $title, body: $body, state: "open", labels: [{name: $label}], comments: []}]' "$issues" > "$issues.tmp"
            mv "$issues.tmp" "$issues"
            jq -n --argjson number "$number" '{number: $number}'
            ;;
          "POST repos/acme/shop/issues/"*"/comments")
            number="${path#repos/acme/shop/issues/}"; number="${number%/comments}"
            jq --argjson number "$number" --arg body "$(field body)" 'map(if .number == $number then .comments += [$body] else . end)' "$issues" > "$issues.tmp"
            mv "$issues.tmp" "$issues"
            echo '{}'
            ;;
          *) echo "fake gh: unexpected call: $method $path" >&2; exit 1 ;;
        esac
        """;

    private const string FirstRun = "https://github.com/acme/shop/actions/runs/1";

    private const string SecondRun = "https://github.com/acme/shop/actions/runs/2";

    private readonly string root = Directory.CreateTempSubdirectory("matrix-issues-test-").FullName;

    private string State => Path.Combine(root, "state");

    private string Results => Path.Combine(root, "results");

    public MatrixIssuesTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(State);
        Directory.CreateDirectory(Results);
        var gh = Path.Combine(root, "bin", "gh");
        File.WriteAllText(gh, FakeGh);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        File.WriteAllText(Path.Combine(State, "issues.json"), "[]");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void ResultOf(string name, string result, string? failedStep, string fingerprint, string supported) =>
        File.WriteAllText(
            Path.Combine(Results, $"{name}.json"),
            $$"""{ "name": "{{name}}", "result": "{{result}}", "failedStep": {{(failedStep is null ? "null" : $"\"{failedStep}\"")}}, "seconds": 60, "fingerprint": "{{fingerprint}}", "supported": {{supported}} }""");

    private (int ExitCode, string Output) Issues(string runUrl) =>
        Shell.Run(
            Repository.Root,
            new Dictionary<string, string>
            {
                ["PATH"] = $"{Path.Combine(root, "bin")}:{Environment.GetEnvironmentVariable("PATH")}",
                ["GH_FAKE_STATE"] = State,
                ["GITHUB_REPOSITORY"] = "acme/shop",
            },
            "bash",
            "build/matrix-issues.sh",
            Results,
            runUrl);

    private JsonArray StoredIssues() => JsonNode.Parse(File.ReadAllText(Path.Combine(State, "issues.json")))!.AsArray();

    [Fact]
    public void A_failing_combination_with_a_supported_option_opens_one_bug_issue()
    {
        ResultOf("pair-a", "failed", "backend build", "f00d", """["backend.database=postgres", "frontend.ui-library=mui"]""");

        var (exitCode, output) = Issues(FirstRun);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("pair-a: opened #1.", Case.Sensitive, output);
        var issue = StoredIssues().ShouldHaveSingleItem()!;
        issue["title"]!.GetValue<string>().ShouldBe("Nightly matrix: pair-a fails at backend build");
        issue["labels"]![0]!["name"]!.GetValue<string>().ShouldBe("bug");
        var body = issue["body"]!.GetValue<string>();
        body.ShouldContain(FirstRun);
        body.ShouldContain("- `backend.database=postgres`");
        body.ShouldContain("- `frontend.ui-library=mui`");
        body.ShouldContain("build/profile-ci.sh --combination pair-a --steps generate,build,test,web");
        body.ShouldContain("<!-- matrix-combination: f00d -->");
    }

    [Fact]
    public void A_rerun_comments_on_the_open_issue_instead_of_opening_a_second()
    {
        ResultOf("pair-a", "failed", "backend build", "f00d", """["backend.database=postgres"]""");
        Issues(FirstRun);
        ResultOf("pair-a", "failed", "pnpm lint", "f00d", """["backend.database=postgres"]""");

        var (exitCode, output) = Issues(SecondRun);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("pair-a: still failing; commented on #1.", Case.Sensitive, output);
        var issue = StoredIssues().ShouldHaveSingleItem()!;
        issue["comments"]!.AsArray().Select(comment => comment!.GetValue<string>())
            .ShouldBe([$"Still failing in {SecondRun}, at pnpm lint."]);
    }

    [Fact]
    public void Another_combination_s_failure_opens_its_own_issue_and_a_closed_one_is_not_reused()
    {
        File.WriteAllText(Path.Combine(State, "issues.json"), """
            [
              { "number": 1, "title": "Nightly matrix: pair-a fails at pnpm test", "body": "<!-- matrix-combination: f00d -->", "state": "closed", "labels": [{ "name": "bug" }], "comments": [] },
              { "number": 2, "title": "Something else", "body": "<!-- matrix-combination: beef -->", "state": "open", "labels": [{ "name": "bug" }], "comments": [] }
            ]
            """);
        ResultOf("pair-a", "failed", "pnpm test", "f00d", """["backend.database=postgres"]""");

        var (exitCode, output) = Issues(FirstRun);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("pair-a: opened #3.", Case.Sensitive, output);
        StoredIssues().Count.ShouldBe(3);
        StoredIssues().ShouldAllBe(issue => issue!["comments"]!.AsArray().Count == 0);
    }

    [Fact]
    public void Passing_combinations_and_failing_ones_of_experimental_options_only_open_nothing()
    {
        ResultOf("pair-a", "passed", null, "f00d", """["backend.database=postgres"]""");
        ResultOf("pair-b", "failed", "backend build", "beef", "[]");

        var (exitCode, output) = Issues(FirstRun);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("pair-b: failed, but holds no supported option; no issue (ADR-0003).", Case.Sensitive, output);
        StoredIssues().ShouldBeEmpty();
    }

    [Fact]
    public void A_combination_that_did_not_generate_opens_an_issue_saying_its_tiers_are_unknown()
    {
        ResultOf("pair-a", "failed", "generate", "f00d", "null");

        var (exitCode, output) = Issues(FirstRun);

        exitCode.ShouldBe(0, output);
        StoredIssues().ShouldHaveSingleItem()!["body"]!.GetValue<string>()
            .ShouldContain("It did not generate, so the run could not tell which of its options are supported.");
    }

    [Fact]
    public void Missing_arguments_are_refused()
    {
        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/matrix-issues.sh", Results);

        exitCode.ShouldBe(2, output);
        output.ShouldContain("Usage: build/matrix-issues.sh <results-directory> <run-url>", Case.Sensitive, output);
    }
}

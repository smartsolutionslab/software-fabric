using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The pairwise matrix's nightly layer (ADR-0003 amendment, roadmap M3-04): .github/workflows/matrix.yml runs one job
/// per combination of build/matrix.json, on a schedule and on demand only. build/matrix-combination.sh puts one
/// combination through build/profile-ci.sh without e2e and writes its result, duration and supported options to a
/// file; build/matrix-report.sh turns those files into the run's table.
/// </summary>
public sealed class MatrixNightlyTests : IDisposable
{
    private const string MatrixJson = """
        {
          "combinations": [
            { "name": "pair-a", "choices": { "backend.api-protocols": ["rest", "grpc"], "backend.test.extras": [], "module.style": "ddd" } },
            { "name": "pair-b", "choices": { "backend.llm": "no-such-option" } },
            { "name": "pair-c", "choices": { "module.style": "crud" } }
          ]
        }
        """;

    private readonly string root = Directory.CreateTempSubdirectory("matrix-nightly-test-").FullName;

    private readonly string matrix;

    public MatrixNightlyTests() => matrix = Write("matrix.json", MatrixJson);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Results => Path.Combine(root, "results");

    private string Write(string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A stand-in for build/profile-ci.sh: records its arguments, prints <paramref name="output"/> and exits with <paramref name="exitCode"/>.</summary>
    private string ProfileCi(string output, int exitCode) =>
        Write("profile-ci.sh", $"""
            #!/usr/bin/env bash
            printf '%s\n' "$@" > "{root}/profile-ci-arguments"
            cat <<'OUTPUT'
            {output}
            OUTPUT
            exit {exitCode}
            """);

    private (int ExitCode, string Output) Combination(string name, string? profileCi = null) => Combination(name, matrix, profileCi);

    private (int ExitCode, string Output) Combination(string name, string matrixFile, string? profileCi)
    {
        Dictionary<string, string> environment = new() { ["TMPDIR"] = root };
        if (profileCi is not null)
        {
            environment["MATRIX_PROFILE_CI"] = profileCi;
        }

        return Shell.Run(Repository.Root, environment, "bash", ["build/matrix-combination.sh", name, Results, "--matrix", matrixFile]);
    }

    private JsonObject Result(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(Results, $"{name}.json")))!.AsObject();

    private static string[] Strings(JsonNode? node) => [.. node!.AsArray().Select(item => item!.GetValue<string>())];

    [Fact]
    public void A_combination_runs_through_profile_ci_without_e2e_and_its_result_is_written()
    {
        var (exitCode, output) = Combination("pair-a", ProfileCi("combination pair-a: generated, built, tested, web app checked", 0));

        exitCode.ShouldBe(0, output);
        File.ReadAllLines(Path.Combine(root, "profile-ci-arguments"))
            .ShouldBe(["--combination", "pair-a", "--matrix", matrix, "--steps", "generate,build,test,web"]);
        var result = Result("pair-a");
        result["name"]!.GetValue<string>().ShouldBe("pair-a");
        result["result"]!.GetValue<string>().ShouldBe("passed");
        result["failedStep"].ShouldBeNull();
        result["seconds"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_failing_step_fails_the_combination_naming_the_step()
    {
        var log = """
            created /tmp/x/shop with 312 files.
            ::group::combination pair-a: backend format check
            ::endgroup::
            ::group::combination pair-a: backend build
            error CS0246: The type or namespace name 'Foo' could not be found
            """;

        var (exitCode, output) = Combination("pair-a", ProfileCi(log, 1));

        exitCode.ShouldBe(1, output);
        var result = Result("pair-a");
        result["result"]!.GetValue<string>().ShouldBe("failed");
        result["failedStep"]!.GetValue<string>().ShouldBe("backend build");
    }

    [Fact]
    public void The_supported_options_are_the_chosen_ones_the_generator_did_not_warn_about()
    {
        var log = """
            created /tmp/x/shop with 312 files.
            warning: backend.api-protocols=grpc is experimental (ADR-0003): it is generated, but not yet passing the test matrix.
            combination pair-a: generated, built, tested, web app checked
            """;

        Combination("pair-a", ProfileCi(log, 0));

        Strings(Result("pair-a")["supported"]).ShouldBe(["backend.api-protocols=rest", "module.style=ddd"]);
    }

    [Fact]
    public void A_combination_that_does_not_generate_fails_at_generate_with_its_tiers_unknown()
    {
        var (exitCode, output) = Combination("pair-b");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'no-such-option' is not an option of backend.llm", Case.Sensitive, output);
        var result = Result("pair-b");
        result["result"]!.GetValue<string>().ShouldBe("failed");
        result["failedStep"]!.GetValue<string>().ShouldBe("generate");
        result["supported"].ShouldBeNull("generation failed before the generator named the experimental options.");
    }

    [Fact]
    public void A_combination_s_fingerprint_is_its_choices_whatever_their_order()
    {
        Combination("pair-a", ProfileCi("ok", 0));
        var fingerprint = Result("pair-a")["fingerprint"]!.GetValue<string>();
        var reorderedMatrix = Write("reordered.json", """{ "combinations": [ { "name": "pair-a", "choices": { "module.style": "ddd", "backend.test.extras": [], "backend.api-protocols": ["rest", "grpc"] } } ] }""");
        Combination("pair-a", reorderedMatrix, Path.Combine(root, "profile-ci.sh"));
        var reordered = Result("pair-a")["fingerprint"]!.GetValue<string>();
        Combination("pair-c", ProfileCi("ok", 0));

        fingerprint.ShouldMatch("^[0-9a-f]{64}$");
        reordered.ShouldBe(fingerprint);
        Result("pair-c")["fingerprint"]!.GetValue<string>().ShouldNotBe(fingerprint);
    }

    [Fact]
    public void A_combination_not_in_the_matrix_is_refused()
    {
        var (exitCode, output) = Combination("pair-z", ProfileCi("ok", 0));

        exitCode.ShouldBe(2, output);
        output.ShouldContain($"error: no combination pair-z in {matrix}.", Case.Sensitive, output);
        File.Exists(Path.Combine(root, "profile-ci-arguments")).ShouldBeFalse();
    }

    [Fact]
    public void The_report_is_a_table_of_result_and_duration_per_combination_in_matrix_order()
    {
        Directory.CreateDirectory(Results);
        File.WriteAllText(Path.Combine(Results, "pair-a.json"), """{ "name": "pair-a", "result": "passed", "failedStep": null, "seconds": 245, "fingerprint": "a", "supported": [] }""");
        File.WriteAllText(Path.Combine(Results, "pair-b.json"), """{ "name": "pair-b", "result": "failed", "failedStep": "backend build", "seconds": 3725, "fingerprint": "b", "supported": null }""");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/matrix-report.sh", Results, "--matrix", matrix);

        exitCode.ShouldBe(0, output);
        output.ShouldBe("""
            ## Pairwise matrix: 1 of 3 combinations passed

            | Combination | Result | Failed at | Duration |
            |---|---|---|---|
            | pair-a | passed |  | 4m 05s |
            | pair-b | failed | backend build | 62m 05s |
            | pair-c | no result |  |  |

            Total time: 66m 10s in 2 jobs.

            """);
    }

    [Fact]
    public void The_workflow_runs_only_nightly_and_on_demand()
    {
        var workflow = Workflow();

        ((YamlMappingNode)workflow["on"]).Children.Keys.Select(key => key.ToString()).Order().ToList()
            .ShouldBe(["schedule", "workflow_dispatch"], "pull requests and pushes run the matrix's generation only; building it there would take their runners.");
    }

    [Fact]
    public void The_workflow_runs_one_job_per_combination_with_capped_parallelism()
    {
        var jobs = (YamlMappingNode)Workflow()["jobs"];
        var listing = Runs((YamlMappingNode)jobs["combinations"]);
        listing.ShouldContain(run => run.Contains("jq", StringComparison.Ordinal) && run.Contains("build/matrix.json", StringComparison.Ordinal));

        var job = (YamlMappingNode)jobs["combination"];
        ((YamlScalarNode)job["needs"]).Value.ShouldBe("combinations");
        var strategy = (YamlMappingNode)job["strategy"];
        ((YamlScalarNode)strategy["fail-fast"]).Value.ShouldBe("false");
        int.Parse(((YamlScalarNode)strategy["max-parallel"]).Value!, System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(1, 4);
        ((YamlScalarNode)((YamlMappingNode)strategy["matrix"])["combination"]).Value.ShouldBe("${{ fromJSON(needs.combinations.outputs.names) }}");
        var runs = Runs(job);
        runs.ShouldContain("dotnet build src/Fabric.Cli/Fabric.Cli.csproj -c Release");
        runs.ShouldContain(run => run.StartsWith("build/matrix-combination.sh ", StringComparison.Ordinal) && !run.Contains("--steps", StringComparison.Ordinal));
    }

    [Fact]
    public void The_workflow_reports_after_every_combination_even_a_failed_one_and_may_open_issues()
    {
        var workflow = Workflow();
        var permissions = (YamlMappingNode)workflow["permissions"];
        ((YamlScalarNode)permissions["contents"]).Value.ShouldBe("read");
        permissions.Children.ShouldNotContainKey(new YamlScalarNode("issues"), "only the report job writes issues.");

        var report = (YamlMappingNode)((YamlMappingNode)workflow["jobs"])["report"];
        ((YamlScalarNode)report["needs"]).Value.ShouldBe("combination");
        ((YamlScalarNode)report["if"]).Value.ShouldBe("${{ !cancelled() }}");
        ((YamlScalarNode)((YamlMappingNode)report["permissions"])["issues"]).Value.ShouldBe("write");
        var concurrency = (YamlMappingNode)report["concurrency"];
        ((YamlScalarNode)concurrency["group"]).Value.ShouldBe("matrix-report", "issue reporting is serialised across refs, so two runs cannot both open an issue for one combination.");
        ((YamlScalarNode)concurrency["cancel-in-progress"]).Value.ShouldBe("false");
        var runs = Runs(report);
        runs.ShouldContain(run => run.StartsWith("build/matrix-report.sh ", StringComparison.Ordinal) && run.Contains(">> \"$GITHUB_STEP_SUMMARY\"", StringComparison.Ordinal));
        runs.ShouldContain(run => run.StartsWith("build/matrix-issues.sh ", StringComparison.Ordinal));
    }

    private static YamlMappingNode Workflow()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "matrix.yml"))));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static List<string> Runs(YamlMappingNode job) =>
    [
        .. ((YamlSequenceNode)job["steps"]).Children.Cast<YamlMappingNode>()
            .Where(step => step.Children.ContainsKey(new YamlScalarNode("run")))
            .Select(step => ((YamlScalarNode)step["run"]).Value!.Trim()),
    ];
}

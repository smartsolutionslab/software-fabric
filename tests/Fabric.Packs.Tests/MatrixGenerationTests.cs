using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The pairwise matrix's per-pull-request layer (ADR-0003 amendment, roadmap M3-03): build/matrix-generate.sh generates
/// every combination of build/matrix.json, in one job, and fails naming each combination that did not generate and its
/// first error.
/// </summary>
public sealed class MatrixGenerationTests
{
    private const string MatrixJson = """
        {
          "combinations": [
            { "name": "pair-bad", "choices": { "backend.llm": "no-such-option" } },
            { "name": "pair-good", "choices": { "module.ddd-depth": "strict" } },
            { "name": "pair-worse", "choices": { "frontend.design-system": "no-such-system", "backend.llm": "no-such-option" } }
          ]
        }
        """;

    [Fact]
    public void A_matrix_whose_every_combination_generates_passes()
    {
        var (exitCode, output) = Generate("""{ "combinations": [ { "name": "pair-good", "choices": { "module.ddd-depth": "strict" } } ] }""");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("pair-good: generated", Case.Sensitive, output);
        output.ShouldContain("every combination generated: 1 of 1", Case.Sensitive, output);
        output.ShouldNotContain("::error", Case.Sensitive, output);
    }

    [Fact]
    public void A_combination_that_fails_to_generate_fails_the_run_naming_the_combination_and_its_first_error()
    {
        var (exitCode, output) = Generate(MatrixJson);

        exitCode.ShouldBe(1, output);
        output.ShouldContain("::error title=matrix generation::pair-bad: error: 'no-such-option' is not an option of backend.llm", Case.Sensitive, output);
        output.ShouldContain("::error title=matrix generation::pair-worse: error: ", Case.Sensitive, output);
        output.Split('\n').Count(line => line.StartsWith("::error title=matrix generation::pair-worse:", StringComparison.Ordinal))
            .ShouldBe(1, "only a combination's first error is reported.");
        output.ShouldContain("2 of 3 combinations failed to generate: pair-bad, pair-worse", Case.Sensitive, output);
    }

    [Fact]
    public void A_failing_combination_does_not_stop_the_ones_after_it()
    {
        var (_, output) = Generate(MatrixJson);

        output.ShouldContain("pair-good: generated", Case.Sensitive, output);
        output.IndexOf("pair-bad", StringComparison.Ordinal).ShouldBeLessThan(output.IndexOf("pair-good: generated", StringComparison.Ordinal), output);
    }

    [Fact]
    public void A_missing_matrix_is_refused()
    {
        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/matrix-generate.sh", "--matrix", "/no/such/matrix.json");

        exitCode.ShouldBe(2, output);
        output.ShouldContain("Usage: build/matrix-generate.sh [--matrix <file>]", Case.Sensitive, output);
    }

    [Fact]
    public void The_workflow_generates_the_matrix_in_one_job_on_every_pull_request()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml"))));
        var jobs = (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode)["jobs"];

        jobs.Children.ShouldContainKey(new YamlScalarNode("matrix-generation"));
        var job = (YamlMappingNode)jobs["matrix-generation"];
        job.Children.ShouldNotContainKey(new YamlScalarNode("strategy"), "the matrix is generated in one job, not one per combination.");
        job.Children.ShouldNotContainKey(new YamlScalarNode("needs"), "nothing gates it; a red result shows at once.");
        var runs = ((YamlSequenceNode)job["steps"]).Children.Cast<YamlMappingNode>()
            .Where(step => step.Children.ContainsKey(new YamlScalarNode("run")))
            .Select(step => ((YamlScalarNode)step["run"]).Value!)
            .ToList();
        runs.ShouldContain("dotnet build src/Fabric.Cli/Fabric.Cli.csproj -c Release");
        runs.ShouldContain("build/matrix-generate.sh");
    }

    /// <summary>Runs build/matrix-generate.sh over <paramref name="matrixJson"/>, written to a temporary directory of its own.</summary>
    private static (int ExitCode, string Output) Generate(string matrixJson)
    {
        var temporary = Directory.CreateTempSubdirectory("matrix-generate-test-");
        try
        {
            var matrix = Path.Combine(temporary.FullName, "matrix.json");
            File.WriteAllText(matrix, matrixJson);
            return Shell.Run(Repository.Root, new Dictionary<string, string> { ["TMPDIR"] = temporary.FullName }, "bash", "build/matrix-generate.sh", "--matrix", matrix);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }
}

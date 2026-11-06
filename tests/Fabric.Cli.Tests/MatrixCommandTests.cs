using System.Text.RegularExpressions;

namespace Fabric.Cli.Tests;

public sealed class MatrixCommandTests : IDisposable
{
    private const string Regenerate =
        "regenerate it with: dotnet run --project src/Fabric.Cli -- matrix --catalog catalog/catalog.yaml --packs packs " +
        "--extra build/matrix-extra.yaml --output build/matrix.json";

    private static readonly string CheckedInMatrix = Path.Combine(Paths.Repository, "build", "matrix.json");

    private static readonly string CheckedInExtra = Path.Combine(Paths.Repository, "build", "matrix-extra.yaml");

    private readonly string root = Directory.CreateTempSubdirectory("fabric-matrix-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static CliRun Matrix(params string[] extra) =>
        CliRun.Of(["matrix", "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks, .. extra]);

    [Fact]
    public void Writes_the_matrix_to_the_output_file_and_prints_what_it_covers()
    {
        var output = Path.Combine(root, "matrix.json");

        var run = Matrix("--output", output);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldMatch($"^wrote {Regex.Escape(output)}: [4-9] combinations cover 23 feasible pairs; 1 pair is infeasible.{Environment.NewLine}$");
        var json = File.ReadAllText(output);
        json.ShouldContain("\"name\": \"pairwise-1\"");
        json.ShouldContain("\"backend.database=postgres\"");
        json.ShouldContain("\"module.style=crud\"");
        json.ShouldContain("\"backend.api-protocols without rest\",\n      \"backend.api-protocols has grpc\"");
        json.ShouldEndWith("}\n");
    }

    [Fact]
    public void Without_an_output_file_prints_the_matrix()
    {
        var output = Path.Combine(root, "matrix.json");
        Matrix("--output", output).ExitCode.ShouldBe(0);

        var run = Matrix();

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldBe(File.ReadAllText(output));
    }

    [Fact]
    public void Puts_the_extra_combinations_first()
    {
        var extra = Path.Combine(root, "matrix-extra.yaml");
        File.WriteAllText(extra, "combinations:\n  - reason: grpc on sqlserver\n    choices:\n      backend.database: sqlserver\n      backend.api-protocols: [rest, grpc]\n");

        var run = Matrix("--extra", extra);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldContain("\"name\": \"extra-1\",\n      \"reason\": \"grpc on sqlserver\"");
    }

    [Fact]
    public void An_extra_combination_that_cannot_be_built_fails_and_writes_nothing()
    {
        var extra = Path.Combine(root, "matrix-extra.yaml");
        var output = Path.Combine(root, "matrix.json");
        File.WriteAllText(extra, "combinations:\n  - reason: grpc alone\n    choices:\n      backend.api-protocols: [grpc]\n");

        var run = Matrix("--extra", extra, "--output", output);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldBe($"error: extra 'grpc alone': no valid choice set contains backend.api-protocols=grpc.{Environment.NewLine}");
        File.Exists(output).ShouldBeFalse();
    }

    /// <summary>
    /// build/matrix.json is what the matrix jobs run (ADR-0003): a change to the catalog, a pack or the extras shows
    /// its effect on the matrix in the same pull request.
    /// </summary>
    [Fact]
    public void The_checked_in_matrix_is_what_the_catalog_the_packs_and_the_extras_give_today()
    {
        var output = Path.Combine(root, "matrix.json");

        var run = CliRun.Of("matrix", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks, "--extra", CheckedInExtra, "--output", output);

        run.ExitCode.ShouldBe(0, run.Error);
        File.Exists(CheckedInMatrix).ShouldBeTrue("build/matrix.json is missing; " + Regenerate);
        File.ReadAllText(CheckedInMatrix).ShouldBe(File.ReadAllText(output), "build/matrix.json is stale; " + Regenerate);
    }
}

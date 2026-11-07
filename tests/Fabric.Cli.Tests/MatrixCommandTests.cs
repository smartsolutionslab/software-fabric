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
        json.ShouldContain("\"choices\": {\n        \"backend.database\": \"postgres\",\n        \"backend.api-protocols\": [");
        json.ShouldContain("\"module.style\": \"crud\"");
        json.ShouldContain("\"backend.api-protocols\": []");
        json.ShouldContain("\"backend.api-protocols without rest\",\n      \"backend.api-protocols has grpc\"");
        json.ShouldEndWith("}\n");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    public void A_packs_directory_without_packs_fails_and_writes_nothing(string packs)
    {
        var directory = Path.Combine(root, packs);
        if (packs == "empty")
        {
            Directory.CreateDirectory(directory);
        }

        var output = Path.Combine(root, "matrix.json");

        var run = CliRun.Of("matrix", "--catalog", Paths.FixtureCatalog, "--packs", directory, "--output", output);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldBe($"error: no packs under {directory}; the matrix covers the options that have one.{Environment.NewLine}");
        File.Exists(output).ShouldBeFalse();
    }

    /// <summary>The reviewer's reproduction: two dimensions without packs added to the fixture catalog, each active only if the other is.</summary>
    [Fact]
    public void A_catalog_with_an_active_when_cycle_fails_naming_the_cycle_and_writes_nothing()
    {
        var catalog = Path.Combine(root, "catalog.yaml");
        File.WriteAllText(
            catalog,
            File.ReadAllText(Paths.FixtureCatalog) + """

                  x.a: { scope: project, active-when: "x.b == on", default: on, options: { on: { tier: supported } } }
                  x.b: { scope: project, active-when: "x.a == on", default: on, options: { on: { tier: supported } } }

                """);
        var output = Path.Combine(root, "matrix.json");

        var run = CliRun.Of("matrix", "--catalog", catalog, "--packs", Paths.FixturePacks, "--output", output);

        run.ExitCode.ShouldBe(1, run.Output);
        run.Error.ShouldBe(
            "error: x.a active-when: cycle x.a -> x.b -> x.a; each dimension is active only if the next one is, so none can be decided." +
            Environment.NewLine);
        File.Exists(output).ShouldBeFalse();
    }

    /// <summary>The reviewer's reproduction: packs for the two options of one single-valued dimension, and no other.</summary>
    [Fact]
    public void Packs_for_only_one_factor_fail_instead_of_writing_an_empty_matrix()
    {
        var packs = Path.Combine(root, "packs");
        foreach (var pack in new[] { Path.Combine("base", "core"), Path.Combine("backend", "database") })
        {
            NewCommandTests.CopyDirectory(Path.Combine(Paths.FixturePacks, pack), Path.Combine(packs, pack));
        }

        var output = Path.Combine(root, "matrix.json");

        var run = CliRun.Of("matrix", "--catalog", Paths.FixtureCatalog, "--packs", packs, "--output", output);

        run.ExitCode.ShouldBe(1, run.Output);
        run.Error.ShouldBe(
            "error: the options with a pack are the values of one factor (backend.database=postgres, backend.database=sqlserver), so there is no pair to cover; " +
            "the matrix needs options with a pack in two dimensions, or two options of a multi-valued one." + Environment.NewLine);
        File.Exists(output).ShouldBeFalse();
    }

    /// <summary>A combination's choices are what <c>fabric new --choices</c> reads: dimension to option, or to a list for a multi-valued one.</summary>
    [Fact]
    public void Each_combination_generates_with_fabric_new_from_its_choices()
    {
        var matrix = Path.Combine(root, "matrix.json");
        Matrix("--output", matrix).ExitCode.ShouldBe(0);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(matrix));

        foreach (var combination in document.RootElement.GetProperty("combinations").EnumerateArray())
        {
            var name = combination.GetProperty("name").GetString()!;
            var choices = Path.Combine(root, $"{name}.json");
            File.WriteAllText(choices, combination.GetProperty("choices").GetRawText());

            var run = CliRun.Of("new", Path.Combine(root, name), "--choices", choices, "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks);

            run.ExitCode.ShouldBe(0, $"{name}: {run.Error}");
            File.ReadAllText(Path.Combine(root, name, "fabric.yaml")).ShouldNotContain("profile:", Case.Sensitive, name);
        }
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

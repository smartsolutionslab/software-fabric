namespace Fabric.Cli.Tests;

public sealed class ReferenceCommandTests : IDisposable
{
    private const string Regenerate =
        "regenerate it with: dotnet run --project src/Fabric.Cli -- reference --catalog catalog/catalog.yaml --output docs/catalog.md";

    private static readonly string CheckedInReference = Path.Combine(Paths.Repository, "docs", "catalog.md");

    private readonly string root = Directory.CreateTempSubdirectory("fabric-reference-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Writes_the_reference_to_the_output_file_and_prints_what_it_lists()
    {
        var output = Path.Combine(root, "catalog.md");

        var run = CliRun.Of("reference", "--catalog", Paths.FixtureCatalog, "--output", output);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldBe($"wrote {output}: 3 dimensions, 6 options, 2 profiles.{Environment.NewLine}");
        var markdown = File.ReadAllText(output);
        markdown.ShouldStartWith("# Catalog reference\n");
        markdown.ShouldContain("| `grpc` | supported | `backend.api-protocols has rest` | — | `everything` | — |\n");
        markdown.ShouldEndWith("|\n");
    }

    [Fact]
    public void Without_an_output_file_prints_the_reference()
    {
        var run = CliRun.Of("reference", "--catalog", Paths.FixtureCatalog);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldStartWith("# Catalog reference\n");
        run.Output.ShouldContain("### `module.style`\n");
    }

    /// <summary>
    /// docs/catalog.md is how a reader learns the catalog without reading its YAML: a change to the catalog shows in it
    /// in the same pull request, like build/matrix.json.
    /// </summary>
    [Fact]
    public void The_checked_in_reference_is_what_the_catalog_gives_today()
    {
        var output = Path.Combine(root, "catalog.md");

        var run = CliRun.Of("reference", "--catalog", Paths.RepositoryCatalog, "--output", output);

        run.ExitCode.ShouldBe(0, run.Error);
        File.Exists(CheckedInReference).ShouldBeTrue("docs/catalog.md is missing; " + Regenerate);
        File.ReadAllText(CheckedInReference).ShouldBe(File.ReadAllText(output), "docs/catalog.md is stale; " + Regenerate);
    }
}

using System.Text.RegularExpressions;

namespace Fabric.Cli.Tests;

/// <summary>
/// ADR-0003: an experimental option is generated with a warning. The warning goes to standard error, names each chosen
/// experimental option once, and changes neither the exit code nor what is written.
/// </summary>
public sealed class ExperimentalWarningTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("fabric-experimental-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>The fixture catalog with the named options moved to the experimental tier; the packs stay the fixture's.</summary>
    private string CatalogWithExperimental(params string[] options)
    {
        var text = File.ReadAllText(Paths.FixtureCatalog);
        foreach (var option in options)
        {
            var marked = Regex.Replace(text, $@"(\b{Regex.Escape(option)}:\s+(?:\{{ )?)tier: supported", "${1}tier: experimental");
            marked.ShouldNotBe(text, $"the fixture catalog has no option '{option}' to mark");
            text = marked;
        }

        var path = Path.Combine(root, $"catalog-{string.Join('-', options)}.yaml");
        File.WriteAllText(path, text);
        return path;
    }

    private static string Warning(string option) =>
        $"warning: {option} is experimental (ADR-0003): it is generated, but not yet passing the test matrix.";

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Validate_warns_about_each_chosen_experimental_option_and_still_exits_zero()
    {
        var catalog = CatalogWithExperimental("sqlserver", "grpc", "crud");

        var run = CliRun.Of("validate", "--profile", "everything", "--catalog", catalog);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldStartWith("valid: profile everything");
        Lines(run.Error).ShouldBe([
            Warning("backend.database=sqlserver"),
            Warning("backend.api-protocols=grpc"),
            Warning("module.style=crud"),
        ]);
    }

    [Fact]
    public void Validate_does_not_warn_about_an_experimental_option_that_was_not_chosen()
    {
        var catalog = CatalogWithExperimental("sqlserver", "grpc", "crud");

        var run = CliRun.Of("validate", "--profile", "minimal", "--catalog", catalog);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Error.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_does_not_warn_when_the_choices_break_a_rule()
    {
        var catalog = CatalogWithExperimental("grpc");

        var run = CliRun.Of("validate", "--set", "backend.api-protocols=grpc", "--catalog", catalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldNotContain("warning:");
    }

    [Fact]
    public void New_warns_about_each_chosen_experimental_option_and_writes_what_it_would_write_without_the_warning()
    {
        var catalog = CatalogWithExperimental("sqlserver", "grpc");

        var warned = CliRun.Of("new", Path.Combine(root, "warned", "shop"), "--profile", "everything", "--catalog", catalog, "--packs", Paths.FixturePacks);
        var plain = CliRun.Of("new", Path.Combine(root, "plain", "shop"), "--profile", "everything", "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks);

        warned.ExitCode.ShouldBe(0, warned.Error);
        Lines(warned.Error).ShouldBe([Warning("backend.database=sqlserver"), Warning("backend.api-protocols=grpc")]);
        warned.Output.Replace(Path.Combine(root, "warned"), "", StringComparison.Ordinal)
            .ShouldBe(plain.Output.Replace(Path.Combine(root, "plain"), "", StringComparison.Ordinal));
        Tree(Path.Combine(root, "warned", "shop")).ShouldBe(Tree(Path.Combine(root, "plain", "shop")));
    }

    [Fact]
    public void New_does_not_warn_when_it_writes_nothing()
    {
        var catalog = CatalogWithExperimental("grpc");

        var run = CliRun.Of("new", Path.Combine(root, "shop"), "--set", "backend.api-protocols=grpc", "--catalog", catalog, "--packs", Paths.FixturePacks);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldNotContain("warning:");
    }

    [Fact]
    public void Add_module_warns_about_each_experimental_option_the_project_then_has()
    {
        var project = Path.Combine(root, "shop");
        CliRun.Of("new", project, "--profile", "minimal", "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks).ExitCode.ShouldBe(0);
        var catalog = CatalogWithExperimental("postgres", "ddd");

        var run = CliRun.Of("add-module", "billing", "--project", project, "--catalog", catalog, "--packs", Paths.FixturePacks);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldBe($"added module billing with 1 file.{Environment.NewLine}");
        Lines(run.Error).ShouldBe([Warning("backend.database=postgres"), Warning("module.style=ddd")]);
    }

    [Fact]
    public void Add_module_does_not_warn_when_every_chosen_option_is_supported()
    {
        var project = Path.Combine(root, "shop");
        CliRun.Of("new", project, "--profile", "minimal", "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks).ExitCode.ShouldBe(0);
        var catalog = CatalogWithExperimental("crud");

        var run = CliRun.Of("add-module", "billing", "--project", project, "--catalog", catalog, "--packs", Paths.FixturePacks);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Error.ShouldBeEmpty();
    }

    private static Dictionary<string, string> Tree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), File.ReadAllText);
}

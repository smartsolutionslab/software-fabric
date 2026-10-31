namespace Fabric.Cli.Tests;

public sealed class NewCommandTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("fabric-new-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private CliRun New(string directory, params string[] extra) =>
        CliRun.Of(["new", Path.Combine(root, directory), "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks, .. extra]);

    private Dictionary<string, string> Tree(string directory)
    {
        var full = Path.Combine(root, directory);
        return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(full, path).Replace('\\', '/'), File.ReadAllText);
    }

    [Fact]
    public void Writes_the_project_and_its_fabric_yaml()
    {
        var run = New("shop", "--profile", "everything");

        run.ExitCode.ShouldBe(0);
        run.Output.ShouldBe($"created {Path.Combine(root, "shop")} with 10 files.{Environment.NewLine}");
        Tree("shop").Keys.ShouldContain("docs/adr/0005-module-style-crud.md");
        Tree("shop")["fabric.yaml"].ShouldContain("name: shop\n");
        Tree("shop")["fabric.yaml"].ShouldContain("backend.api-protocols: [rest, grpc]\n");
    }

    [Fact]
    public void Prints_the_readmes_first_steps_after_writing_the_project()
    {
        var packs = Path.Combine(root, "packs");
        CopyDirectory(Paths.FixturePacks, packs);
        File.AppendAllText(
            Path.Combine(packs, "base", "core", "templates", "readme.md.sbn"),
            "\n## First steps\n\n- Install the tools and commit the lockfile.\n\n## Afterwards\n\nNot a first step.\n");

        var run = CliRun.Of(["new", Path.Combine(root, "shop"), "--profile", "everything", "--catalog", Paths.FixtureCatalog, "--packs", packs]);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldContain("First steps (README.md):");
        run.Output.ShouldContain("- Install the tools and commit the lockfile.");
        run.Output.ShouldNotContain("Not a first step.");
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    [Fact]
    public void Two_runs_with_the_same_choices_write_identical_trees()
    {
        New("first", "--profile", "everything", "--name", "shop").ExitCode.ShouldBe(0);
        New("second", "--profile", "everything", "--name", "shop").ExitCode.ShouldBe(0);

        Tree("second").ShouldBe(Tree("first"));
    }

    [Fact]
    public void Refuses_a_directory_that_is_not_empty()
    {
        Directory.CreateDirectory(Path.Combine(root, "busy"));
        File.WriteAllText(Path.Combine(root, "busy", "keep.txt"), "mine");

        var run = New("busy", "--profile", "minimal");

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("is not empty");
        Tree("busy").Keys.ShouldBe(["keep.txt"]);
    }

    [Fact]
    public void An_invalid_choice_writes_nothing()
    {
        var run = New("broken", "--profile", "minimal", "--set", "backend.api-protocols=grpc");

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: backend.api-protocols=grpc requires backend.api-protocols has rest");
        Directory.Exists(Path.Combine(root, "broken")).ShouldBeFalse();
    }

    [Fact]
    public void A_profile_whose_packs_do_not_exist_yet_reports_each_missing_pack()
    {
        var run = CliRun.Of("new", Path.Combine(root, "real"), "--profile", "lean-crud", "--catalog", Paths.RepositoryCatalog, "--packs", Path.Combine(root, "no-packs"));

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: no pack for backend.database=postgres.");
    }

    [Fact]
    public void The_name_option_wins_over_the_directory_name()
    {
        var run = New("Some_Directory", "--profile", "minimal", "--name", "shop");

        run.ExitCode.ShouldBe(0);
        Tree("Some_Directory")["AGENTS.md"].ShouldStartWith("# Agent guide for shop\n");
    }

    [Fact]
    public void A_directory_name_that_is_not_a_valid_project_name_needs_the_name_option()
    {
        var run = New("Some_Directory", "--profile", "minimal");

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: project name 'Some_Directory' must be lower-case letters, digits and hyphens, starting with a letter. Pass --name.");
    }
}

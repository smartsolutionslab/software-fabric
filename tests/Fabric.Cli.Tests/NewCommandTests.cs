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
    public void A_project_without_an_llm_generates_and_tells_its_agents_so()
    {
        var run = CliRun.Of("new", Path.Combine(root, "plain"), "--profile", "lean-crud", "--set", "backend.llm=none", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(0, run.Error);
        File.ReadAllText(Path.Combine(root, "plain", "AGENTS.md")).ShouldContain("## LLM: none");
    }

    [Fact]
    public void A_project_choosing_rag_writes_nothing_and_names_the_rag_pack_it_still_lacks()
    {
        var run = CliRun.Of("new", Path.Combine(root, "rag"), "--profile", "lean-crud", "--set", "backend.llm=openai", "--set", "backend.llm.extras=rag", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: no pack for backend.llm.extras=rag.");
        run.Error.ShouldNotContain("no pack for backend.llm.embeddings");
        Directory.Exists(Path.Combine(root, "rag")).ShouldBeFalse();
    }

    [Fact]
    public void A_project_calling_a_model_without_extras_records_that_it_chose_none()
    {
        var run = CliRun.Of("new", Path.Combine(root, "model"), "--profile", "lean-crud", "--set", "backend.llm=openai", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(0, run.Error);
        var fabric = File.ReadAllText(Path.Combine(root, "model", "fabric.yaml"));
        fabric.ShouldContain("backend.llm.extras: []");
        fabric.ShouldNotContain("backend.llm.embeddings");
    }

    [Fact]
    public void A_project_choosing_evals_writes_nothing_and_names_the_pack_it_still_lacks()
    {
        var run = CliRun.Of("new", Path.Combine(root, "evals"), "--profile", "lean-crud", "--set", "backend.llm=openai", "--set", "backend.llm.extras=evals", "--set", "backend.llm.extras.evals.threshold=3.5", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldBe($"error: no pack for backend.llm.extras=evals.{Environment.NewLine}");
        Directory.Exists(Path.Combine(root, "evals")).ShouldBeFalse();
    }

    [Fact]
    public void A_threshold_outside_its_range_is_refused_before_anything_is_written()
    {
        var run = CliRun.Of("new", Path.Combine(root, "evals"), "--profile", "lean-crud", "--set", "backend.llm=openai", "--set", "backend.llm.extras=evals", "--set", "backend.llm.extras.evals.threshold=9", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldBe($"error: backend.llm.extras.evals.threshold is 9, but it must be between 1 and 5.{Environment.NewLine}");
        Directory.Exists(Path.Combine(root, "evals")).ShouldBeFalse();
    }

    [Fact]
    public void A_setting_set_on_the_command_line_is_recorded_in_fabric_yaml()
    {
        var run = New("shop", "--profile", "everything", "--set", "backend.api-protocols.grpc.deadline-seconds=12.5");

        run.ExitCode.ShouldBe(0, run.Error);
        Tree("shop")["fabric.yaml"].ShouldContain("settings:\n  backend.api-protocols.grpc.deadline-seconds: 12.5\nmodule-defaults:");
    }

    [Fact]
    public void A_project_without_settings_has_no_settings_in_fabric_yaml()
    {
        New("shop", "--profile", "minimal").ExitCode.ShouldBe(0);

        Tree("shop")["fabric.yaml"].ShouldNotContain("settings");
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

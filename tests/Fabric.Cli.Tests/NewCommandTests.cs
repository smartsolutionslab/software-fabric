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
    public void A_project_choosing_rag_generates_with_its_embeddings_following_the_model_provider()
    {
        var run = CliRun.Of("new", Path.Combine(root, "rag"), "--profile", "lean-crud", "--set", "backend.llm=openai", "--set", "backend.llm.extras=rag", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(0, run.Error);
        var fabric = File.ReadAllText(Path.Combine(root, "rag", "fabric.yaml"));
        fabric.ShouldContain("backend.llm.extras: [rag]");
        fabric.ShouldContain("backend.llm.embeddings: openai");
        File.Exists(Path.Combine(root, "rag", "src", "BuildingBlocks", "Rag", "ModuleVectorIndex.cs")).ShouldBeTrue();
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
    public void A_project_choosing_evals_records_the_threshold_it_set_and_gets_the_eval_project()
    {
        var run = CliRun.Of("new", Path.Combine(root, "evals"), "--profile", "lean-crud", "--set", "backend.llm=openai", "--set", "backend.llm.extras=evals", "--set", "backend.llm.extras.evals.threshold=3.5", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(0, run.Error);
        File.ReadAllText(Path.Combine(root, "evals", "fabric.yaml")).ShouldContain("settings:\n  backend.llm.extras.evals.threshold: 3.5\n");
        File.Exists(Path.Combine(root, "evals", "tests", "Evals", "Evals.csproj")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "evals", ".github", "workflows", "evals.yml")).ShouldBeTrue();
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

    private string ChoiceSet(string json)
    {
        var path = Path.Combine(root, "choices.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void A_choice_set_generates_the_project_its_profile_would_and_records_no_profile()
    {
        var choices = ChoiceSet("""{ "backend.database": "sqlserver", "backend.api-protocols": ["rest", "grpc"], "module.style": "crud" }""");

        var run = New("from-choices", "--name", "shop", "--choices", choices);
        New("from-profile", "--name", "shop", "--profile", "everything").ExitCode.ShouldBe(0);

        run.ExitCode.ShouldBe(0, run.Error);
        Tree("from-choices")["fabric.yaml"].ShouldNotContain("profile:");
        Tree("from-choices")["fabric.yaml"].ShouldBe(Tree("from-profile")["fabric.yaml"].Replace("profile: everything\n", string.Empty, StringComparison.Ordinal));
        Tree("from-choices").Where(file => file.Key != "fabric.yaml").ShouldBe(Tree("from-profile").Where(file => file.Key != "fabric.yaml"));
    }

    [Fact]
    public void An_override_wins_over_the_choice_set()
    {
        var choices = ChoiceSet("""{ "backend.database": "sqlserver" }""");

        var run = New("shop", "--choices", choices, "--set", "backend.database=postgres");

        run.ExitCode.ShouldBe(0, run.Error);
        Tree("shop")["fabric.yaml"].ShouldContain("backend.database: postgres\n");
    }

    [Fact]
    public void A_choice_set_with_a_profile_is_refused_before_anything_is_written()
    {
        var choices = ChoiceSet("""{ "backend.database": "sqlserver" }""");

        var run = New("shop", "--choices", choices, "--profile", "minimal");

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldBe($"error: --choices is a whole choice set and takes no --profile; pass one or the other.{Environment.NewLine}");
        Directory.Exists(Path.Combine(root, "shop")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{ "module.style": 3 }""", "module.style must be an option or a list of options")]
    [InlineData("""{ "backend.api-protocols": ["rest", 1] }""", "backend.api-protocols must be an option or a list of options")]
    [InlineData("""["backend.database=postgres"]""", "must be a JSON object of dimension to option")]
    [InlineData("""{ "backend.database": """, "is not valid JSON")]
    public void A_choice_set_that_is_not_dimensions_to_options_is_refused_naming_the_file(string json, string message)
    {
        var choices = ChoiceSet(json);

        var run = New("shop", "--choices", choices);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldStartWith($"error: {choices}: ");
        run.Error.ShouldContain(message);
        Directory.Exists(Path.Combine(root, "shop")).ShouldBeFalse();
    }

    [Fact]
    public void A_choice_set_naming_an_option_the_catalog_lacks_is_refused_like_an_override()
    {
        var choices = ChoiceSet("""{ "backend.database": "oracle" }""");

        var run = New("shop", "--choices", choices);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("'oracle' is not an option of backend.database");
        Directory.Exists(Path.Combine(root, "shop")).ShouldBeFalse();
    }
}

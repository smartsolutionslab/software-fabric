using System.Reflection;

namespace Fabric.Cli.Tests;

/// <summary>
/// <c>fabric upgrade</c> (ADR-0016 E4, D1, D2, D3, D8): the project is generated with "fabric 0.0.1" (the fixture packs),
/// edited, committed, and upgraded to this build with packs that changed: the README's title, a new project file
/// <c>docs/notes.md</c>, and a new per-module file <c>src/&lt;module&gt;/page.test.md</c>.
/// </summary>
public sealed class UpgradeCommandTests : IDisposable
{
    private const string OldVersion = "0.0.1";

    private static readonly string Running =
        typeof(FabricCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    private static readonly string[] NewArguments =
        ["--profile", "minimal", "--set", "backend.api-protocols=rest,grpc", "--set", "backend.api-protocols.grpc.deadline-seconds=45"];

    private static readonly string[] Modules = ["shipping", "billing"];

    private readonly string root = Directory.CreateTempSubdirectory("fabric-upgrade-").FullName;

    private readonly string project;

    private readonly string newPacks;

    private readonly string oldFabric;

    public UpgradeCommandTests()
    {
        project = Path.Combine(root, "shop");
        newPacks = Path.Combine(root, "packs");
        CopyDirectory(Paths.FixturePacks, newPacks);
        Replace(Path.Combine(newPacks, "base", "core", "templates", "readme.md.sbn"), "# Generated project\n", "# Generated project, upgraded\n");
        AddFile(Path.Combine(newPacks, "base", "core"), "notes.md.sbn", "docs/notes.md", perModule: false, "Notes the generator keeps.\n");
        AddFile(Path.Combine(newPacks, "module", "style", "ddd"), "page-test.sbn", "src/{{ module.name }}/page.test.md", perModule: true, "# {{ module.name }} page test\n");

        oldFabric = OldFabric.Create(root, OldVersion, Paths.FixtureCatalog, Paths.FixturePacks);
        Generate(oldFabric, project);
        Git.Run(project, "init", "-q");
        Git.Commit(project, "generate with fabric 0.0.1");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private static void Replace(string path, string oldText, string newText)
    {
        var text = File.ReadAllText(path);
        text.ShouldContain(oldText);
        File.WriteAllText(path, text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    private static void AddFile(string pack, string template, string target, bool perModule, string content)
    {
        File.WriteAllText(Path.Combine(pack, "templates", template), content);
        var entry = perModule
            ? $"  - {{ template: templates/{template}, target: \"{target}\", per-module: true }}\n"
            : $"  - {{ template: templates/{template}, target: {target} }}\n";
        Replace(Path.Combine(pack, "pack.yaml"), "files:\n", "files:\n" + entry);
    }

    /// <summary><c>fabric new</c> and <c>add-module</c> for each module, by the script of an old fabric.</summary>
    private static void Generate(string fabric, string directory)
    {
        Run(fabric, ["new", directory, .. NewArguments]);
        foreach (var module in Modules)
        {
            Run(fabric, ["add-module", module, "--project", directory]);
        }
    }

    private static void Run(string fabric, string[] arguments)
    {
        var (exitCode, output, error) = Programs.Run(fabric, Path.GetDirectoryName(fabric)!, arguments);
        exitCode.ShouldBe(0, output + error);
    }

    /// <summary>What this build writes for the project from scratch: the tree an unedited project must upgrade to (D1 b).</summary>
    private Dictionary<string, string> Fresh()
    {
        var fresh = Path.Combine(root, "fresh", "shop");
        CliRun.Of(["new", fresh, .. NewArguments, "--catalog", Paths.FixtureCatalog, "--packs", newPacks]).ExitCode.ShouldBe(0);
        foreach (var module in Modules)
        {
            CliRun.Of("add-module", module, "--project", fresh, "--catalog", Paths.FixtureCatalog, "--packs", newPacks).ExitCode.ShouldBe(0);
        }

        return Tree(fresh);
    }

    private static Dictionary<string, string> Tree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(path => !path.StartsWith(".git/", StringComparison.Ordinal))
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(directory, path)));

    private CliRun Upgrade(params string[] extra) =>
        CliRun.Of(["upgrade", "--project", project, .. extra, "--catalog", Paths.FixtureCatalog, "--packs", newPacks]);

    private CliRun UpgradeFromOld() => Upgrade("--from", oldFabric);

    private string Read(string path) => File.ReadAllText(Path.Combine(project, path));

    private void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(project, path))!);
        File.WriteAllText(Path.Combine(project, path), content);
    }

    private string Unmerged() => Git.Run(project, "ls-files", "-u");

    /// <summary>The stages git holds for an unmerged path: 1 base, 2 the project, 3 the generator.</summary>
    private List<string> Stages(string path) =>
        [.. Unmerged().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.EndsWith("\t" + path, StringComparison.Ordinal))
            .Select(line => line.Split('\t')[0].Split(' ')[2])];

    private void ShouldBeUntouched(string head)
    {
        Git.Run(project, "status", "--porcelain").ShouldBeEmpty();
        Git.Run(project, "rev-parse", "HEAD").ShouldBe(head);
    }

    [Fact]
    public void An_unedited_project_comes_out_exactly_as_this_fabric_writes_it_fresh()
    {
        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        run.Error.ShouldBeEmpty();
        run.Output.ShouldStartWith($"upgraded shop from fabric {OldVersion} to fabric {Running}; nothing is committed.");
        Tree(project).ShouldBe(Fresh());
        Unmerged().ShouldBeEmpty();
        Read("fabric.yaml").ShouldContain($"\ngenerator: {Running}\n", Case.Sensitive);
        run.Output.ShouldContain("  M README.md");
        run.Output.ShouldContain("  A docs/notes.md");
        run.Output.ShouldContain("  A src/billing/page.test.md");
        run.Output.ShouldNotContain("conflict");
        run.Output.ShouldNotContain("builds");
    }

    [Fact]
    public void An_edit_the_pack_change_does_not_overlap_is_kept_and_the_pack_change_merged_into_it()
    {
        Write("README.md", Read("README.md") + "Our own notes.\n");
        Git.Commit(project, "our notes");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        Read("README.md").ShouldBe(Fresh()["README.md"] + "Our own notes.\n");
        Unmerged().ShouldBeEmpty();
        run.Output.ShouldContain($"merged with the project's edits (1):{Environment.NewLine}  M README.md{Environment.NewLine}");
        run.Output.ShouldContain("that the project builds and passes its tests: it has edits of its own");
    }

    [Fact]
    public void A_conflicting_edit_is_left_as_a_git_conflict_with_both_sides_and_named_in_the_summary()
    {
        Write("README.md", Read("README.md").Replace("# Generated project\n", "# Our shop\n", StringComparison.Ordinal));
        Git.Commit(project, "our title");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        Read("README.md").ShouldStartWith(
            $"<<<<<<< project\n# Our shop\n||||||| fabric {OldVersion}\n# Generated project\n=======\n" +
            $"# Generated project, upgraded\n>>>>>>> fabric {Running}\n\nDatabase: postgres\n");
        Stages("README.md").ShouldBe(["1", "2", "3"]);
        run.Output.ShouldContain($"conflicts to resolve, then git add and commit (1):{Environment.NewLine}  both changed: README.md{Environment.NewLine}");
    }

    [Fact]
    public void A_file_both_sides_added_differently_is_a_conflict_with_both_sides_not_a_failed_merge_of_a_missing_empty_blob()
    {
        Write("docs/notes.md", "Our notes.\n");
        Git.Commit(project, "our notes");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        run.Error.ShouldBeEmpty();
        Read("docs/notes.md").ShouldBe(
            $"<<<<<<< project\nOur notes.\n||||||| fabric {OldVersion}\n=======\nNotes the generator keeps.\n>>>>>>> fabric {Running}\n");
        Stages("docs/notes.md").ShouldBe(["2", "3"]);
        run.Output.ShouldContain("  both added: docs/notes.md");
    }

    [Fact]
    public void A_generated_file_at_a_path_the_project_already_owns_in_a_module_is_a_conflict_the_summary_names()
    {
        Write("src/billing/page.test.md", "Our billing page test.\n");
        Git.Commit(project, "our page test");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        Read("src/billing/page.test.md").ShouldContain("Our billing page test.\n");
        Read("src/billing/page.test.md").ShouldContain("# billing page test\n");
        Stages("src/billing/page.test.md").ShouldBe(["2", "3"]);
        Read("src/shipping/page.test.md").ShouldBe("# shipping page test\n");
        run.Output.ShouldContain("  both added: src/billing/page.test.md");
        run.Output.ShouldContain("  A src/shipping/page.test.md");
    }

    [Fact]
    public void A_file_the_project_deleted_and_the_generator_changed_is_a_conflict_not_a_silent_deletion()
    {
        File.Delete(Path.Combine(project, "README.md"));
        Git.Commit(project, "no readme");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        Stages("README.md").ShouldBe(["1", "3"]);
        run.Output.ShouldContain($"  deleted by the project, changed by fabric {Running}: README.md");
    }

    [Fact]
    public void A_file_the_project_made_a_symlink_is_left_as_a_conflict_and_its_target_never_written()
    {
        var outside = Path.Combine(root, "outside.md");
        File.WriteAllText(outside, "Outside the project.\n");
        File.Delete(Path.Combine(project, "README.md"));
        File.CreateSymbolicLink(Path.Combine(project, "README.md"), outside);
        Git.Commit(project, "readme is a link");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        File.ReadAllText(outside).ShouldBe("Outside the project.\n");
        new FileInfo(Path.Combine(project, "README.md")).LinkTarget.ShouldBe(outside);
        Stages("README.md").ShouldBe(["1", "2", "3"]);
        run.Output.ShouldContain("  both changed: README.md");
    }

    [Fact]
    public void Choices_recorded_in_another_key_order_are_the_same_choices()
    {
        Write("fabric.yaml", Read("fabric.yaml").Replace(
            "  backend.database: postgres\n  backend.api-protocols: [rest, grpc]\n",
            "  backend.api-protocols: [rest, grpc]\n  backend.database: postgres\n",
            StringComparison.Ordinal));
        Git.Commit(project, "keys in another order");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        Unmerged().ShouldBeEmpty();
    }

    [Fact]
    public void An_experimental_option_is_warned_about_as_new_and_add_module_warn()
    {
        var catalog = Path.Combine(root, "catalog.yaml");
        File.WriteAllText(catalog, File.ReadAllText(Paths.FixtureCatalog).Replace(
            "      grpc:\n        tier: supported\n",
            "      grpc:\n        tier: experimental\n",
            StringComparison.Ordinal));

        var run = CliRun.Of("upgrade", "--project", project, "--from", oldFabric, "--catalog", catalog, "--packs", newPacks);

        run.ExitCode.ShouldBe(0, run.Error);
        run.Error.ShouldContain("warning: backend.api-protocols=grpc is experimental (ADR-0003)");
    }

    [Fact]
    public void Upgrading_the_same_project_twice_gives_the_same_bytes()
    {
        Write("README.md", Read("README.md").Replace("# Generated project\n", "# Our shop\n", StringComparison.Ordinal));
        Write("docs/notes.md", "Our notes.\n");
        Git.Commit(project, "our edits");
        var head = Git.Run(project, "rev-parse", "HEAD");
        var first = UpgradeFromOld();
        first.ExitCode.ShouldBe(0, first.Error);
        Stages("docs/notes.md").ShouldBe(["2", "3"]);
        var firstTree = Tree(project);
        Git.Run(project, "reset", "-q", "--hard", head.Trim());

        var second = UpgradeFromOld();

        second.Output.ShouldBe(first.Output);
        Tree(project).ShouldBe(firstTree);
    }

    [Fact]
    public void Prints_what_d1_does_not_guarantee_only_where_it_applies()
    {
        Write("README.md", Read("README.md") + "Our own notes.\n");
        Git.Commit(project, "our notes");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldContain("not guaranteed (ADR-0016 D1):");
        run.Output.ShouldContain("that the project builds and passes its tests: it has edits of its own");
        run.Output.ShouldNotContain("pnpm-lock.yaml");
        run.Output.ShouldNotContain("migration");
        run.Output.ShouldNotContain("openapi.json");
    }

    [Fact]
    public void Names_the_openapi_document_and_its_client_when_csharp_under_src_changed_in_a_project_that_has_the_document()
    {
        AddFile(Path.Combine(newPacks, "base", "core"), "openapi.sbn", "web/app/openapi.json", perModule: false, "{}\n");
        AddFile(Path.Combine(newPacks, "module", "style", "ddd"), "endpoint.sbn", "src/{{ module.name }}/Endpoint.cs", perModule: true, "// endpoint\n");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldContain(
            "  - that web/app/openapi.json and the client generated from it match what the host serves: C# under src/ changed; " +
            "run the integration tests, which rewrite the document, commit it, then run pnpm codegen in web/app and adapt the screens.\n");
    }

    [Fact]
    public void Does_not_name_the_openapi_document_for_a_project_without_one()
    {
        AddFile(Path.Combine(newPacks, "module", "style", "ddd"), "endpoint.sbn", "src/{{ module.name }}/Endpoint.cs", perModule: true, "// endpoint\n");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldContain("migration");
        run.Output.ShouldNotContain("openapi.json");
    }

    [Fact]
    public void Does_not_name_the_openapi_document_when_no_csharp_under_src_changed()
    {
        AddFile(Path.Combine(newPacks, "base", "core"), "openapi.sbn", "web/app/openapi.json", perModule: false, "{}\n");
        AddFile(Path.Combine(newPacks, "base", "core"), "test.sbn", "tests/Integration.Tests/SomethingTests.cs", perModule: false, "// a test\n");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldContain("migration");
        run.Output.ShouldNotContain("that web/app/openapi.json");
    }

    [Fact]
    public void A_fabric_yaml_without_a_generator_version_is_refused_and_the_project_left_alone()
    {
        Write("fabric.yaml", string.Join('\n', Read("fabric.yaml").Split('\n').Where(line => !line.StartsWith("generator: ", StringComparison.Ordinal))));
        Git.Commit(project, "an old fabric.yaml");
        var head = Git.Run(project, "rev-parse", "HEAD");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("fabric.yaml records no generator version, so the tree that generated the project cannot be reproduced");
        ShouldBeUntouched(head);
    }

    [Fact]
    public void A_development_version_is_refused_because_it_was_never_published()
    {
        Write("fabric.yaml", Read("fabric.yaml").Replace($"generator: {OldVersion}", "generator: 0.0.1-dev", StringComparison.Ordinal));
        Git.Commit(project, "a development build");
        var head = Git.Run(project, "rev-parse", "HEAD");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("fabric.yaml was written by fabric 0.0.1-dev, a development build that is never published");
        ShouldBeUntouched(head);
    }

    [Fact]
    public void A_catalog_hash_the_recorded_version_does_not_write_is_refused()
    {
        var recorded = Read("fabric.yaml").Split('\n').Single(line => line.StartsWith("catalog: ", StringComparison.Ordinal));
        Write("fabric.yaml", Read("fabric.yaml").Replace(recorded, "catalog: sha256:" + new string('0', 64), StringComparison.Ordinal));
        Git.Commit(project, "another catalog");
        var head = Git.Run(project, "rev-parse", "HEAD");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain($"fabric.yaml records catalog sha256:{new string('0', 64)}, but fabric {OldVersion} writes catalog {recorded["catalog: ".Length..]}");
        ShouldBeUntouched(head);
    }

    [Fact]
    public void An_old_generator_that_is_not_the_recorded_version_is_refused()
    {
        Write("fabric.yaml", Read("fabric.yaml").Replace($"generator: {OldVersion}", "generator: 0.0.2", StringComparison.Ordinal));
        Git.Commit(project, "another version");
        var head = Git.Run(project, "rev-parse", "HEAD");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain($"{oldFabric} is fabric {OldVersion}, but fabric.yaml was written by fabric 0.0.2");
        ShouldBeUntouched(head);
    }

    [Fact]
    public void A_version_that_cannot_be_installed_from_the_feed_is_named_with_the_way_round_it()
    {
        var head = Git.Run(project, "rev-parse", "HEAD");

        var run = Upgrade();

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain($"could not install fabric {OldVersion} from https://nuget.pkg.github.com/smartsolutionslab/index.json");
        run.Error.ShouldContain($"--from <path to fabric {OldVersion}>");
        ShouldBeUntouched(head);
    }

    [Fact]
    public void A_project_with_uncommitted_changes_is_refused_and_left_alone()
    {
        Write("README.md", "Not committed.\n");

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("has uncommitted changes; commit or stash them first, so the upgrade is the only change");
        Read("README.md").ShouldBe("Not committed.\n");
    }

    [Fact]
    public void A_project_that_is_not_the_top_of_a_git_repository_is_refused()
    {
        Directory.Delete(Path.Combine(project, ".git"), recursive: true);

        var run = UpgradeFromOld();

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("is not the top of a git repository");
    }
}

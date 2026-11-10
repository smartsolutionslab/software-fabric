namespace Fabric.Cli.Tests;

public sealed class AddModuleCommandTests : IDisposable
{
    private readonly string project = Path.Combine(Directory.CreateTempSubdirectory("fabric-add-").FullName, "shop");

    public AddModuleCommandTests()
    {
        CliRun.Of("new", project, "--profile", "minimal", "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks).ExitCode.ShouldBe(0);
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(project)!, recursive: true);

    private CliRun AddModule(params string[] extra) =>
        CliRun.Of(["add-module", .. extra, "--project", project, "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks]);

    [Fact]
    public void Creates_the_module_and_updates_fabric_yaml()
    {
        var run = AddModule("billing");

        run.ExitCode.ShouldBe(0);
        run.Output.ShouldBe($"added module billing with 1 file.{Environment.NewLine}");
        File.ReadAllText(Path.Combine(project, "src", "billing", "README.md")).ShouldBe("# billing (ddd)\n");
        File.ReadAllText(Path.Combine(project, "fabric.yaml")).ShouldEndWith("modules:\n  billing:\n    module.style: ddd\n");
    }

    [Fact]
    public void Keeps_the_generator_version_and_catalog_hash_in_fabric_yaml()
    {
        var before = Stamp(File.ReadAllText(Path.Combine(project, "fabric.yaml")));

        AddModule("billing").ExitCode.ShouldBe(0);

        before.Length.ShouldBe(2);
        Stamp(File.ReadAllText(Path.Combine(project, "fabric.yaml"))).ShouldBe(before);
    }

    [Fact]
    public void Adding_the_same_module_twice_fails()
    {
        AddModule("billing").ExitCode.ShouldBe(0);

        var run = AddModule("billing");

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: module 'billing' already exists.");
    }

    [Fact]
    public void A_directory_without_fabric_yaml_fails()
    {
        var run = CliRun.Of("add-module", "billing", "--project", Path.GetDirectoryName(project)!, "--catalog", Paths.FixtureCatalog, "--packs", Paths.FixturePacks);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("fabric.yaml");
    }

    [Fact]
    public void An_existing_file_at_a_module_path_fails_and_leaves_fabric_yaml_alone()
    {
        Directory.CreateDirectory(Path.Combine(project, "src", "billing"));
        File.WriteAllText(Path.Combine(project, "src", "billing", "README.md"), "mine");
        var before = File.ReadAllText(Path.Combine(project, "fabric.yaml"));

        var run = AddModule("billing");

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: src/billing/README.md already exists");
        File.ReadAllText(Path.Combine(project, "fabric.yaml")).ShouldBe(before);
    }

    [Fact]
    public void A_module_without_integration_events_is_refused_and_named()
    {
        var root = Directory.CreateTempSubdirectory("fabric-add-").FullName;
        try
        {
            var shop = Path.Combine(root, "shop");
            CliRun AddModuleToShop(params string[] extra) =>
                CliRun.Of(["add-module", .. extra, "--project", shop, "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks]);
            CliRun.Of("new", shop, "--profile", "lean-crud", "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks).ExitCode.ShouldBe(0);
            AddModuleToShop("billing").ExitCode.ShouldBe(0);

            var run = AddModuleToShop("shipping", "--set", "module.communication=");

            run.ExitCode.ShouldBe(1);
            run.Error.ShouldContain("error: module 'shipping': module.communication requires module.communication has integration-events, but module.communication is empty.");
            run.Error.ShouldNotContain("module 'billing'");
            Directory.Exists(Path.Combine(shop, "src", "Modules", "Shipping")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The lines of fabric.yaml that say which generator and which catalog wrote it.</summary>
    private static string[] Stamp(string fabricYaml) =>
        [.. fabricYaml.Split('\n').Where(line => line.StartsWith("generator: ", StringComparison.Ordinal) || line.StartsWith("catalog: sha256:", StringComparison.Ordinal))];
}

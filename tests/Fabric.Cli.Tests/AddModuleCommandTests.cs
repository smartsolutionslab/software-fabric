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
}

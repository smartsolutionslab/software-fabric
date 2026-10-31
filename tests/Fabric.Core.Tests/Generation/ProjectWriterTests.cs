using System.Text;
using Fabric.Core.Generation;
using Fabric.Core.Rendering;

namespace Fabric.Core.Tests.Generation;

public sealed class ProjectWriterTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("fabric-write-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Writes_files_as_utf8_without_bom_creating_directories()
    {
        var target = Path.Combine(root, "project");

        ProjectWriter.Write(target, [new RenderedFile("src/billing/README.md", "# billing — ä\n")]).ShouldBeNull();

        File.ReadAllBytes(Path.Combine(target, "src", "billing", "README.md")).ShouldBe(Encoding.UTF8.GetBytes("# billing — ä\n"));
    }

    [Fact]
    public void Writes_into_an_existing_empty_directory()
    {
        ProjectWriter.Write(root, [new RenderedFile("a.txt", "a")]).ShouldBeNull();

        File.Exists(Path.Combine(root, "a.txt")).ShouldBeTrue();
    }

    [Fact]
    public void Refuses_a_directory_that_is_not_empty_and_writes_nothing()
    {
        File.WriteAllText(Path.Combine(root, "existing.txt"), "keep");

        ProjectWriter.Write(root, [new RenderedFile("a.txt", "a")]).ShouldBe($"{root} is not empty; fabric new only writes into an empty or new directory.");

        Directory.GetFiles(root).Select(Path.GetFileName).ShouldBe(["existing.txt"]);
    }

    [Fact]
    public void Refuses_a_path_outside_the_directory_and_writes_nothing()
    {
        var target = Path.Combine(root, "project");

        ProjectWriter.Write(target, [new RenderedFile("a.txt", "a"), new RenderedFile("../escape.txt", "x")])
            .ShouldBe("../escape.txt points outside the project directory.");

        Directory.Exists(target).ShouldBeFalse();
        File.Exists(Path.Combine(root, "escape.txt")).ShouldBeFalse();
    }

    [Fact]
    public void Adding_writes_new_files_and_replaces_fabric_yaml()
    {
        File.WriteAllText(Path.Combine(root, "fabric.yaml"), "old");

        ProjectWriter.Add(root, [new RenderedFile("src/billing/README.md", "# billing\n")], "new").ShouldBeNull();

        File.ReadAllText(Path.Combine(root, "src", "billing", "README.md")).ShouldBe("# billing\n");
        File.ReadAllText(Path.Combine(root, "fabric.yaml")).ShouldBe("new");
    }

    [Fact]
    public void Adding_refuses_to_overwrite_an_existing_file_and_writes_nothing()
    {
        File.WriteAllText(Path.Combine(root, "fabric.yaml"), "old");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "b.txt"), "mine");

        ProjectWriter.Add(root, [new RenderedFile("src/a.txt", "a"), new RenderedFile("src/b.txt", "b")], "new")
            .ShouldBe("src/b.txt already exists; add-module does not overwrite files.");

        File.Exists(Path.Combine(root, "src", "a.txt")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(root, "fabric.yaml")).ShouldBe("old");
    }
}

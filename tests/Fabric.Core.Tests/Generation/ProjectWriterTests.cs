using System.Text;
using System.Text.Json.Nodes;
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
    [Fact]
    public void Adding_extends_the_frontends_openapi_copy_with_the_new_modules_paths()
    {
        File.WriteAllText(Path.Combine(root, "fabric.yaml"), "old");
        Directory.CreateDirectory(Path.Combine(root, "web", "app"));
        File.WriteAllText(Path.Combine(root, "web", "app", "openapi.json"), "{\n  \"paths\": {}\n}\n");
        var additions = new OpenApiAdditions([new("/billing/permissions/manage", JsonNode.Parse("{}")!)], [], []);

        ProjectWriter.Add(root, [new RenderedFile("src/billing/README.md", "# billing\n")], "new", additions).ShouldBeNull();

        File.ReadAllText(Path.Combine(root, "web", "app", "openapi.json"))
            .ShouldBe("{\n  \"paths\": {\n    \"/billing/permissions/manage\": {}\n  }\n}\n");
        File.ReadAllText(Path.Combine(root, "fabric.yaml")).ShouldBe("new");
    }

    [Fact]
    public void Adding_refuses_paths_the_openapi_copy_already_has_and_writes_nothing()
    {
        File.WriteAllText(Path.Combine(root, "fabric.yaml"), "old");
        Directory.CreateDirectory(Path.Combine(root, "web", "app"));
        const string Copy = "{\n  \"paths\": {\n    \"/billing/permissions/manage\": {}\n  }\n}\n";
        File.WriteAllText(Path.Combine(root, "web", "app", "openapi.json"), Copy);
        var additions = new OpenApiAdditions([new("/billing/permissions/manage", JsonNode.Parse("{}")!)], [], []);

        ProjectWriter.Add(root, [new RenderedFile("src/billing/README.md", "# billing\n")], "new", additions)
            .ShouldBe("web/app/openapi.json already has the path /billing/permissions/manage; add-module only adds a new module's paths and schemas.");

        File.Exists(Path.Combine(root, "src", "billing", "README.md")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(root, "web", "app", "openapi.json")).ShouldBe(Copy);
        File.ReadAllText(Path.Combine(root, "fabric.yaml")).ShouldBe("old");
    }

    [Fact]
    public void Adding_paths_to_a_project_without_the_openapi_copy_is_refused_and_writes_nothing()
    {
        File.WriteAllText(Path.Combine(root, "fabric.yaml"), "old");
        var additions = new OpenApiAdditions([new("/billing/permissions/manage", JsonNode.Parse("{}")!)], [], []);

        ProjectWriter.Add(root, [new RenderedFile("src/billing/README.md", "# billing\n")], "new", additions)
            .ShouldBe("web/app/openapi.json is missing; add-module adds the new module's paths to it, so restore it first.");

        File.Exists(Path.Combine(root, "src", "billing", "README.md")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(root, "fabric.yaml")).ShouldBe("old");
    }
}

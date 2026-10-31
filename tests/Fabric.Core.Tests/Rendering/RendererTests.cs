using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Core.Tests.Rendering;

public sealed class RendererTests : IDisposable
{
    private readonly string packDirectory = Directory.CreateTempSubdirectory("fabric-render-").FullName;

    public void Dispose() => Directory.Delete(packDirectory, recursive: true);

    private static readonly CatalogModel Catalog = CatalogReader.Read(
        new StringReader("""
            version: 1
            dimensions:
              backend.database: { scope: project, options: { postgres: { tier: supported } } }
              backend.api-protocols: { scope: project, multi: true, options: { rest: { tier: supported }, grpc: { tier: supported } } }
              module.style: { scope: module, options: { ddd: { tier: supported } } }
            """),
        "test.yaml");

    private static readonly ProjectChoices Choices = new(
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["backend.database"] = ["postgres"],
            ["backend.api-protocols"] = ["rest", "grpc"],
        },
        new Dictionary<string, IReadOnlyList<string>> { ["module.style"] = ["ddd"] },
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
        {
            ["billing"] = new Dictionary<string, IReadOnlyList<string>> { ["module.style"] = ["ddd"] },
        });

    private PackManifest Pack(string option, params (string Path, string Content)[] templates)
    {
        foreach (var (path, content) in templates)
        {
            File.WriteAllText(Path.Combine(packDirectory, path), content);
        }

        return new PackManifest(
            option,
            packDirectory,
            [],
            [],
            [],
            new PackAgents("agents.md", new Dictionary<string, string>()),
            "adr.md",
            [new PackGate(GateKind.UnitTest, "renders", null)]);
    }

    private static Composition Compose(
        IReadOnlyList<ComposedFile> files,
        Dictionary<string, IReadOnlyList<SlotEntry>>? slots = null) =>
        new([], files, slots ?? [], []);

    private static ComposedFile Composed(PackManifest pack, string template, string target, string? module = null) =>
        new(pack, new PackFile(template, target, module is not null), target, module);

    private RenderedFile RenderSingle(string template)
    {
        var pack = Pack("base.core", ("t.sbn", template));
        var rendered = Renderer.Render(Catalog, Compose([Composed(pack, "t.sbn", "out.txt")]), Choices);
        rendered.Errors.ShouldBeEmpty();
        return rendered.Files.ShouldHaveSingleItem();
    }

    private string SingleError(string template)
    {
        var pack = Pack("base.core", ("t.sbn", template));
        var rendered = Renderer.Render(Catalog, Compose([Composed(pack, "t.sbn", "out.txt")]), Choices);
        rendered.Files.ShouldBeEmpty();
        return rendered.Errors.ShouldHaveSingleItem();
    }

    [Fact]
    public void Renders_project_values()
    {
        RenderSingle("Database: {{ project[\"backend.database\"] }}").Content.ShouldBe("Database: postgres");
    }

    [Fact]
    public void A_multi_valued_dimension_renders_as_a_list()
    {
        RenderSingle("{{ project[\"backend.api-protocols\"] | array.join \",\" }}").Content.ShouldBe("rest,grpc");
    }

    [Fact]
    public void Module_defaults_and_the_module_list_are_available()
    {
        RenderSingle("{{ module_defaults[\"module.style\"] }} {{ for m in modules }}{{ m.name }}={{ m.values[\"module.style\"] }}{{ end }}")
            .Content.ShouldBe("ddd billing=ddd");
    }

    [Fact]
    public void A_per_module_file_sees_its_module()
    {
        var pack = Pack("module.style.ddd", ("d.sbn", "namespace {{ module.name }}; // {{ module.values[\"module.style\"] }}"));

        var rendered = Renderer.Render(Catalog, Compose([Composed(pack, "d.sbn", "src/billing/Domain.cs", module: "billing")]), Choices);

        rendered.Files.ShouldHaveSingleItem().Content.ShouldBe("namespace billing; // ddd");
    }

    [Fact]
    public void Slot_contributions_render_in_order_joined_by_newlines()
    {
        var owner = Pack("base.core", ("readme.sbn", "# Readme\n{{ slots[\"readme.sections\"] }}\n"));
        var first = Pack("backend.database.postgres", ("pg.sbn", "## {{ project[\"backend.database\"] }}"));
        var second = Pack("module.style.ddd", ("ddd.sbn", "## DDD"));

        var rendered = Renderer.Render(Catalog, 
            Compose(
                [Composed(owner, "readme.sbn", "README.md")],
                new() { ["readme.sections"] = [new(first, new("readme.sections", "pg.sbn", 0)), new(second, new("readme.sections", "ddd.sbn", 1))] }),
            Choices);

        rendered.Files.ShouldHaveSingleItem().Content.ShouldBe("# Readme\n## postgres\n## DDD\n");
    }

    [Fact]
    public void An_empty_slot_renders_as_an_empty_string()
    {
        var owner = Pack("base.core", ("readme.sbn", "[{{ slots[\"readme.sections\"] }}]"));

        var rendered = Renderer.Render(Catalog, Compose([Composed(owner, "readme.sbn", "README.md")], new() { ["readme.sections"] = [] }), Choices);

        rendered.Files.ShouldHaveSingleItem().Content.ShouldBe("[]");
    }

    [Fact]
    public void An_undefined_variable_is_an_error_naming_the_pack_and_template()
    {
        SingleError("{{ projekt }}").ShouldStartWith("base.core: t.sbn: ");
    }

    [Theory]
    [InlineData("{{ date.now }}")]
    [InlineData("{{ timespan.from_days 1 }}")]
    public void Clock_functions_are_not_available(string template)
    {
        SingleError(template).ShouldStartWith("base.core: t.sbn: ");
    }

    [Fact]
    public void A_template_that_does_not_parse_is_an_error()
    {
        SingleError("{{ if }}").ShouldStartWith("base.core: t.sbn: ");
    }

    [Fact]
    public void Line_endings_are_normalised_to_lf()
    {
        RenderSingle("a\r\nb\r\n").Content.ShouldBe("a\nb\n");
    }

    [Fact]
    public void Files_are_ordered_by_path()
    {
        var pack = Pack("base.core", ("t.sbn", "x"));

        var rendered = Renderer.Render(Catalog, Compose([Composed(pack, "t.sbn", "src/b.txt"), Composed(pack, "t.sbn", "README.md"), Composed(pack, "t.sbn", "src/a.txt")]), Choices);

        rendered.Files.Select(file => file.Path).ShouldBe(["README.md", "src/a.txt", "src/b.txt"]);
    }

    [Fact]
    public void The_same_input_renders_byte_identical_output()
    {
        var pack = Pack("base.core", ("t.sbn", "{{ for p in project }}{{ p.key }}={{ p.value }}\n{{ end }}"));
        var composition = Compose([Composed(pack, "t.sbn", "out.txt")]);

        var first = Renderer.Render(Catalog, composition, Choices).Files.ShouldHaveSingleItem().Content;
        var second = Renderer.Render(Catalog, composition, Choices).Files.ShouldHaveSingleItem().Content;

        second.ShouldBe(first);
        first.ShouldContain("backend.database=postgres");
    }
}

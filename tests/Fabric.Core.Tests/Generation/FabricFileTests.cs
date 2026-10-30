using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Core.Tests.Generation;

public class FabricFileTests
{
    private static Dictionary<string, IReadOnlyList<string>> Values(params (string Dimension, string[] Options)[] values) =>
        values.ToDictionary(value => value.Dimension, value => (IReadOnlyList<string>)value.Options);

    private static readonly ProjectChoices Choices = new(
        Values(("backend.database", ["postgres"]), ("backend.api-protocols", ["rest"])),
        Values(("module.style", ["ddd"])),
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
        {
            ["billing"] = Values(("module.style", ["crud"])),
        });

    [Fact]
    public void Writes_every_choice_with_multi_valued_dimensions_as_lists()
    {
        FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", Choices).ShouldBe("""
            # Written by fabric. These choices are fixed for this project (ADR-0001 of the generator).
            fabric: 1
            name: shop
            profile: minimal
            project:
              backend.database: postgres
              backend.api-protocols: [rest]
            module-defaults:
              module.style: ddd
            modules:
              billing:
                module.style: crud

            """);
    }

    [Fact]
    public void Writes_no_profile_line_and_empty_modules_when_there_are_none()
    {
        var text = FabricFile.Write(Fixtures.MinimalCatalog, "shop", null, Choices with { Modules = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>() });

        text.ShouldNotContain("profile:");
        text.ShouldEndWith("modules: {}\n");
    }

    [Fact]
    public void Quotes_values_another_yaml_reader_would_take_for_booleans_or_numbers()
    {
        var catalog = CatalogReader.Read(
            new StringReader("""
                version: 1
                dimensions:
                  frontend.pwa: { scope: project, options: { "no": { tier: supported }, "yes": { tier: supported } } }
                  x.version: { scope: project, options: { "2": { tier: supported } } }
                """),
            "test.yaml");
        var choices = new ProjectChoices(Values(("frontend.pwa", ["no"]), ("x.version", ["2"])), Values(), new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>());

        var text = FabricFile.Write(catalog, "shop", null, choices);

        text.ShouldContain("frontend.pwa: \"no\"\n");
        text.ShouldContain("x.version: \"2\"\n");
    }

    [Fact]
    public void Reads_back_what_it_wrote()
    {
        var record = FabricFile.Read(new StringReader(FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", Choices)), "fabric.yaml");

        record.Name.ShouldBe("shop");
        record.Profile.ShouldBe("minimal");
        record.Choices.Project["backend.api-protocols"].ShouldBe(["rest"]);
        record.Choices.ModuleDefaults["module.style"].ShouldBe(["ddd"]);
        record.Choices.Modules["billing"]["module.style"].ShouldBe(["crud"]);
    }

    [Fact]
    public void Rejects_an_unknown_field()
    {
        Should.Throw<FabricFileException>(() => FabricFile.Read(new StringReader("fabric: 1\nname: shop\nproject: {}\nmodule-defaults: {}\nmodules: {}\ncolour: red\n"), "fabric.yaml"))
            .Message.ShouldContain("'colour'");
    }

    [Fact]
    public void Rejects_an_unsupported_version()
    {
        Should.Throw<FabricFileException>(() => FabricFile.Read(new StringReader("fabric: 2\nname: shop\nproject: {}\nmodule-defaults: {}\nmodules: {}\n"), "fabric.yaml"))
            .Message.ShouldContain("version 2");
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        Should.Throw<FabricFileException>(() => FabricFile.Read(new StringReader("fabric: 1\nproject: {}\nmodule-defaults: {}\nmodules: {}\n"), "fabric.yaml"))
            .Message.ShouldContain("'name'");
    }
}

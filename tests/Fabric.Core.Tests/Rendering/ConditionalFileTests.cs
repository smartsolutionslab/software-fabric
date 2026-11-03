using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;
using Fabric.Core.Tests.Generation;

namespace Fabric.Core.Tests.Rendering;

/// <summary>A pack.yaml file entry with <c>when</c>: loaded from YAML, composed and rendered like any other.</summary>
public sealed class ConditionalFileTests : IDisposable
{
    private readonly string packsRoot = Directory.CreateTempSubdirectory("fabric-when-").FullName;

    private static readonly ProjectChoices Choices = new(
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["backend.database"] = ["postgres"],
            ["backend.api-protocols"] = ["rest"],
        },
        new Dictionary<string, IReadOnlyList<string>> { ["module.style"] = ["ddd"] },
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
        {
            ["billing"] = new Dictionary<string, IReadOnlyList<string>> { ["module.style"] = ["crud"] },
            ["sales"] = new Dictionary<string, IReadOnlyList<string>> { ["module.style"] = ["ddd"] },
        });

    public ConditionalFileTests()
    {
        var source = Path.Combine(Fixtures.MinimalRoot, "packs");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(packsRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    public void Dispose() => Directory.Delete(packsRoot, recursive: true);

    /// <summary>Adds a file entry with the given condition to the fixture's base.core pack, then loads, composes and renders.</summary>
    private RenderedProject Render(string when, string content = "extra", string target = "extra.txt", bool perModule = false)
    {
        var core = Path.Combine(packsRoot, "base", "core");
        File.WriteAllText(Path.Combine(core, "templates", "extra.sbn"), content);
        var manifest = Path.Combine(core, "pack.yaml");
        var entry = $"""
            files:
              - template: templates/extra.sbn
                target: '{target}'
                per-module: {(perModule ? "true" : "false")}
                when: '{when.Replace("'", "''", StringComparison.Ordinal)}'

            """;
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("files:\n", entry, StringComparison.Ordinal));

        var loaded = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog);
        loaded.Errors.ShouldBeEmpty();
        var composition = Composer.Compose(Fixtures.MinimalCatalog, loaded.Packs, Choices);
        composition.Errors.ShouldBeEmpty();
        return Renderer.Render(Fixtures.MinimalCatalog, composition, Choices, "shop");
    }

    private static string[] Paths(RenderedProject rendered) => [.. rendered.Files.Select(file => file.Path)];

    [Fact]
    public void The_schema_accepts_when_on_a_file_entry()
    {
        Render("true").Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_whose_condition_is_false_is_not_written()
    {
        var rendered = Render("""project["backend.database"] == "sqlserver" """);

        rendered.Errors.ShouldBeEmpty();
        Paths(rendered).ShouldNotContain("extra.txt");
        Paths(rendered).ShouldContain("README.md");
    }

    [Fact]
    public void A_file_whose_condition_is_true_is_written_as_rendered()
    {
        var rendered = Render("""project["backend.database"] == "postgres" """, content: "db={{ project[\"backend.database\"] }}");

        rendered.Errors.ShouldBeEmpty();
        rendered.Files.Single(file => file.Path == "extra.txt").Content.ShouldBe("db=postgres");
    }

    [Fact]
    public void A_file_whose_condition_is_true_and_renders_empty_is_still_written()
    {
        var rendered = Render("true", content: string.Empty);

        rendered.Errors.ShouldBeEmpty();
        rendered.Files.Single(file => file.Path == "extra.txt").Content.ShouldBe(string.Empty);
    }

    [Fact]
    public void A_per_module_condition_sees_the_module()
    {
        var rendered = Render(
            """module.values["module.style"] == "crud" """,
            content: "{{ module.name }}",
            target: "modules/{{ module.name }}.txt",
            perModule: true);

        rendered.Errors.ShouldBeEmpty();
        Paths(rendered).ShouldContain("modules/billing.txt");
        Paths(rendered).ShouldNotContain("modules/sales.txt");
    }

    [Fact]
    public void A_condition_that_does_not_parse_is_an_error_naming_the_pack_and_the_file()
    {
        var rendered = Render("""project["backend.database" == """);

        rendered.Files.ShouldBeEmpty();
        rendered.Errors.ShouldHaveSingleItem().ShouldStartWith("base.core: templates/extra.sbn (extra.txt): when ");
    }

    [Fact]
    public void A_condition_that_is_not_a_boolean_is_an_error_naming_the_pack_and_the_file()
    {
        var rendered = Render("""project["backend.database"]""");

        rendered.Files.ShouldBeEmpty();
        var error = rendered.Errors.ShouldHaveSingleItem();
        error.ShouldStartWith("base.core: templates/extra.sbn (extra.txt): when ");
        error.ShouldContain("must be true or false, got 'postgres'");
    }

    [Fact]
    public void A_condition_reading_an_unknown_variable_is_an_error_naming_the_pack_and_the_file()
    {
        var rendered = Render("""projekt["backend.database"] == "postgres" """);

        rendered.Files.ShouldBeEmpty();
        rendered.Errors.ShouldHaveSingleItem().ShouldStartWith("base.core: templates/extra.sbn (extra.txt): when ");
    }
}

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Core.Tests.Generation;

public class FabricFileTests
{
    /// <summary>SemVer 2.0 without build metadata: a git SHA there would make every checkout write different bytes.</summary>
    private const string SemVerLine = @"^generator: \d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$";

    /// <summary>The version the generator's assembly reports, which every build of this checkout shares.</summary>
    private static readonly string GeneratorVersion =
        typeof(FabricFile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    /// <summary>SHA-256 of the fixture catalog's text, worked out here independently of the generator.</summary>
    private static readonly string CatalogHash =
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Fixtures.MinimalRoot, "catalog.yaml"))));

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
        FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", Choices).ShouldBe($"""
            # Written by fabric. These choices are fixed for this project (ADR-0001 of the generator).
            fabric: 1
            generator: {GeneratorVersion}
            catalog: {CatalogHash}
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
    public void Records_the_generator_version_as_semver_without_build_metadata()
    {
        var generator = FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", Choices).Split('\n')[2];

        generator.ShouldMatch(SemVerLine);
    }

    [Fact]
    public void The_catalog_hash_does_not_depend_on_the_catalogs_line_endings()
    {
        var text = File.ReadAllText(Path.Combine(Fixtures.MinimalRoot, "catalog.yaml"));
        var withLf = CatalogReader.Read(new StringReader(text), "catalog.yaml");
        var withCrLf = CatalogReader.Read(new StringReader(text.Replace("\n", "\r\n", StringComparison.Ordinal)), "catalog.yaml");

        var written = FabricFile.Write(withCrLf, "shop", "minimal", Choices);

        written.ShouldContain($"\ncatalog: {CatalogHash}\n", Case.Sensitive);
        written.ShouldBe(FabricFile.Write(withLf, "shop", "minimal", Choices));
    }

    [Fact]
    public void The_catalog_hash_changes_with_any_change_to_the_catalogs_text()
    {
        var text = File.ReadAllText(Path.Combine(Fixtures.MinimalRoot, "catalog.yaml"));
        var changed = CatalogReader.Read(new StringReader(text + "# a comment is a change too\n"), "catalog.yaml");
        var expected = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text + "# a comment is a change too\n")));

        FabricFile.Write(changed, "shop", "minimal", Choices).ShouldContain($"\ncatalog: {expected}\n", Case.Sensitive);
    }

    [Fact]
    public void Reads_a_file_written_before_the_generator_and_catalog_were_recorded()
    {
        var record = FabricFile.Read(new StringReader("fabric: 1\nname: shop\nproject: {}\nmodule-defaults: {}\nmodules: {}\n"), "fabric.yaml");

        record.Name.ShouldBe("shop");
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
    public void Writes_settings_after_the_project_choices_when_a_chosen_option_declares_them()
    {
        var choices = Choices with
        {
            Settings = new Dictionary<string, decimal> { ["backend.api-protocols.rest.page-size"] = 50m, ["backend.api-protocols.rest.ratio"] = 2.50m },
        };

        FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", choices).ShouldBe($"""
            # Written by fabric. These choices are fixed for this project (ADR-0001 of the generator).
            fabric: 1
            generator: {GeneratorVersion}
            catalog: {CatalogHash}
            name: shop
            profile: minimal
            project:
              backend.database: postgres
              backend.api-protocols: [rest]
            settings:
              backend.api-protocols.rest.page-size: 50
              backend.api-protocols.rest.ratio: 2.5
            module-defaults:
              module.style: ddd
            modules:
              billing:
                module.style: crud

            """);
    }

    [Fact]
    public void Writes_no_settings_line_when_there_are_none()
    {
        FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", Choices).ShouldNotContain("settings");
    }

    [Fact]
    public void Reads_back_the_settings_it_wrote()
    {
        var choices = Choices with { Settings = new Dictionary<string, decimal> { ["backend.api-protocols.rest.ratio"] = 3.5m } };

        var record = FabricFile.Read(new StringReader(FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", choices)), "fabric.yaml");

        record.Choices.Settings.ShouldBe(new Dictionary<string, decimal> { ["backend.api-protocols.rest.ratio"] = 3.5m });
    }

    [Fact]
    public void Reads_no_settings_from_a_file_without_them()
    {
        FabricFile.Read(new StringReader(FabricFile.Write(Fixtures.MinimalCatalog, "shop", "minimal", Choices)), "fabric.yaml")
            .Choices.Settings.ShouldBeEmpty();
    }

    [Fact]
    public void Rejects_a_setting_that_is_not_a_number()
    {
        Should.Throw<FabricFileException>(() => FabricFile.Read(
                new StringReader("fabric: 1\nname: shop\nproject: {}\nsettings:\n  x.y.o.threshold: high\nmodule-defaults: {}\nmodules: {}\n"),
                "fabric.yaml"))
            .Message.ShouldBe("fabric.yaml:5: invalid value 'high' for 'x.y.o.threshold' on 'settings'; expected a number.");
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

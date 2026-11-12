using Fabric.Core.Catalog;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Core.Tests.Generation;

/// <summary>
/// <c>fabric upgrade</c> regenerates a project from its <c>fabric.yaml</c> alone (ADR-0016 E4): the same <c>fabric new</c>
/// and <c>add-module</c> calls, every choice and setting spelled out, the modules in the order they were added.
/// </summary>
public sealed class RegenerateTests : IDisposable
{
    private readonly string packsRoot = Directory.CreateTempSubdirectory("fabric-regenerate-").FullName;

    public RegenerateTests()
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

    private static FabricRecord Record(IEnumerable<RenderedFile> files) =>
        Record(files.Single(file => file.Path == FabricFile.FileName).Content);

    private static FabricRecord Record(string fabricYaml) => FabricFile.Read(new StringReader(fabricYaml), FabricFile.FileName);

    /// <summary><c>fabric new</c> and then <c>add-module</c> for each name, in order, as a person would run them.</summary>
    private static List<RenderedFile> NewThenAdd(
        IReadOnlyDictionary<string, PackManifest> packs,
        string? profile,
        IReadOnlyList<string> overrides,
        params string[] modules)
    {
        var generated = Generator.New(Fixtures.MinimalCatalog, packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        List<RenderedFile> files = [.. generated.Files];
        foreach (var module in modules)
        {
            var addition = Generator.AddModule(Fixtures.MinimalCatalog, packs, Record(files), module, []);
            addition.Errors.ShouldBeEmpty();
            files = [.. files.Where(file => file.Path != FabricFile.FileName), .. addition.Created, new(FabricFile.FileName, addition.FabricYaml!)];
        }

        return [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    [Fact]
    public void Regenerating_from_fabric_yaml_writes_what_new_and_add_module_wrote()
    {
        var written = NewThenAdd(
            Fixtures.MinimalPacks,
            "minimal",
            ["backend.api-protocols=rest,grpc", "backend.api-protocols.grpc.deadline-seconds=45"],
            "shipping",
            "billing");

        var regenerated = Generator.Regenerate(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, Record(written));

        regenerated.Errors.ShouldBeEmpty();
        regenerated.Files.ShouldBe(written);
    }

    [Fact]
    public void Regenerating_a_project_without_a_profile_writes_no_profile_either()
    {
        var written = NewThenAdd(Fixtures.MinimalPacks, null, ["backend.database=sqlserver"], "billing");

        var regenerated = Generator.Regenerate(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, Record(written));

        regenerated.Errors.ShouldBeEmpty();
        regenerated.Files.ShouldBe(written);
    }

    [Fact]
    public void A_module_a_pack_brings_is_generated_by_new_and_not_added_a_second_time()
    {
        var manifest = Path.Combine(packsRoot, "backend", "api-protocols", "rest", "pack.yaml");
        File.WriteAllText(manifest, "modules: { reports: { module.style: crud } }\n" + File.ReadAllText(manifest));
        var loaded = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog);
        loaded.Errors.ShouldBeEmpty();
        var written = NewThenAdd(loaded.Packs, "minimal", [], "billing");

        var regenerated = Generator.Regenerate(Fixtures.MinimalCatalog, loaded.Packs, Record(written));

        regenerated.Errors.ShouldBeEmpty();
        regenerated.Files.ShouldBe(written);
    }

    [Fact]
    public void A_module_a_pack_now_brings_with_other_choices_than_recorded_is_refused_not_silently_changed()
    {
        var manifest = Path.Combine(packsRoot, "backend", "api-protocols", "rest", "pack.yaml");
        var original = File.ReadAllText(manifest);
        File.WriteAllText(manifest, "modules: { reports: { module.style: crud } }\n" + original);
        var before = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog);
        before.Errors.ShouldBeEmpty();
        var written = NewThenAdd(before.Packs, "minimal", []);
        File.WriteAllText(manifest, "modules: { reports: { module.style: ddd } }\n" + original);
        var after = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog);
        after.Errors.ShouldBeEmpty();

        var regenerated = Generator.Regenerate(Fixtures.MinimalCatalog, after.Packs, Record(written));

        regenerated.Files.ShouldBeEmpty();
        regenerated.Errors.ShouldBe([
            "module 'reports' records module.style=crud, but a pack brings it with module.style=ddd; a module's choices are fixed (ADR-0001), so it cannot be regenerated.",
        ]);
    }

    [Fact]
    public void A_profile_the_catalog_no_longer_has_is_kept_as_the_record_says_and_the_recorded_choices_regenerated()
    {
        var written = NewThenAdd(Fixtures.MinimalPacks, "minimal", ["backend.api-protocols=rest,grpc"], "billing");
        var renamed = CatalogReader.Read(
            new StringReader(File.ReadAllText(Path.Combine(Fixtures.MinimalRoot, "catalog.yaml")).Replace("  minimal:\n", "  smallest:\n", StringComparison.Ordinal)),
            "catalog.yaml");
        renamed.Profiles.ContainsKey("minimal").ShouldBeFalse();

        var regenerated = Generator.Regenerate(renamed, Fixtures.MinimalPacks, Record(written));

        regenerated.Errors.ShouldBeEmpty();
        regenerated.Files.Where(file => file.Path != FabricFile.FileName).ShouldBe(written.Where(file => file.Path != FabricFile.FileName));
        var fabricYaml = regenerated.Files.Single(file => file.Path == FabricFile.FileName).Content;
        fabricYaml.ShouldContain("\nprofile: minimal\n", Case.Sensitive);
        fabricYaml[fabricYaml.IndexOf("\nname: ", StringComparison.Ordinal)..].ShouldBe(
            written.Single(file => file.Path == FabricFile.FileName).Content[fabricYaml.IndexOf("\nname: ", StringComparison.Ordinal)..]);
    }

    /// <summary>The fixture catalog with replacements applied to its text, in order.</summary>
    private static CatalogModel Catalog(params (string OldText, string NewText)[] replacements)
    {
        var text = File.ReadAllText(Path.Combine(Fixtures.MinimalRoot, "catalog.yaml"));
        foreach (var (oldText, newText) in replacements)
        {
            text.ShouldContain(oldText);
            text = text.Replace(oldText, newText, StringComparison.Ordinal);
        }

        return CatalogReader.Read(new StringReader(text), "catalog.yaml");
    }

    private const string CacheDimension =
        "dimensions:\n  backend.cache:\n    scope: project\n    default: none\n    options:\n      none: { tier: supported }\n      redis: { tier: supported }\n";

    /// <summary>The packs with an empty pack for each <c>backend.cache</c> option, loaded against <paramref name="catalog"/>.</summary>
    private IReadOnlyDictionary<string, PackManifest> CachePacks(CatalogModel catalog)
    {
        foreach (var option in (string[])["none", "redis"])
        {
            var directory = Path.Combine(packsRoot, "backend", "cache", option);
            Directory.CreateDirectory(Path.Combine(directory, "docs"));
            File.WriteAllText(Path.Combine(directory, "docs", "agents.md"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "docs", "adr.md"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "pack.yaml"), $$"""
                option: backend.cache.{{option}}
                files: []
                agents: { agents-md: docs/agents.md }
                adr: docs/adr.md
                gates:
                  - { kind: unit-test, describes: "Fixture gate." }
                """);
        }

        var loaded = PackLoader.Load(packsRoot, catalog);
        loaded.Errors.ShouldBeEmpty();
        return loaded.Packs;
    }

    private static string ProjectLine(GenerationResult generated, string dimension) =>
        generated.Files.Single(file => file.Path == FabricFile.FileName).Content.Split('\n')
            .Single(line => line.StartsWith($"  {dimension}: ", StringComparison.Ordinal));

    [Fact]
    public void A_dimension_the_record_lacks_takes_the_recorded_profiles_value_while_the_profile_exists()
    {
        var written = NewThenAdd(Fixtures.MinimalPacks, "minimal", []);
        var catalog = Catalog(
            ("dimensions:\n", CacheDimension),
            ("  minimal:\n    backend.database: postgres\n", "  minimal:\n    backend.database: postgres\n    backend.cache: redis\n"));

        var regenerated = Generator.Regenerate(catalog, CachePacks(catalog), Record(written));

        regenerated.Errors.ShouldBeEmpty();
        ProjectLine(regenerated, "backend.cache").ShouldBe("  backend.cache: redis");
    }

    [Fact]
    public void A_dimension_the_record_lacks_takes_the_catalog_default_once_the_profile_is_gone()
    {
        var written = NewThenAdd(Fixtures.MinimalPacks, "minimal", []);
        var catalog = Catalog(
            ("dimensions:\n", CacheDimension),
            ("  minimal:\n    backend.database: postgres\n", "  smallest:\n    backend.database: postgres\n    backend.cache: redis\n"));

        var regenerated = Generator.Regenerate(catalog, CachePacks(catalog), Record(written));

        regenerated.Errors.ShouldBeEmpty();
        ProjectLine(regenerated, "backend.cache").ShouldBe("  backend.cache: none");
        regenerated.Files.Single(file => file.Path == FabricFile.FileName).Content.ShouldContain("\nprofile: minimal\n", Case.Sensitive);
    }

    [Fact]
    public void A_module_dimension_the_catalog_added_reaches_a_module_a_pack_brings_as_it_reaches_one_added_by_hand()
    {
        var manifest = Path.Combine(packsRoot, "backend", "api-protocols", "rest", "pack.yaml");
        File.WriteAllText(manifest, "modules: { reports: { module.style: crud } }\n" + File.ReadAllText(manifest));
        var loaded = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog);
        loaded.Errors.ShouldBeEmpty();
        var written = NewThenAdd(loaded.Packs, "minimal", [], "billing");
        var catalog = Catalog(("dimensions:\n",
            "dimensions:\n  module.audit:\n    scope: module\n    multi: true\n    default: []\n    options:\n      events: { tier: supported }\n"));

        var regenerated = Generator.Regenerate(catalog, loaded.Packs, Record(written));

        regenerated.Errors.ShouldBeEmpty();
        regenerated.Files.Single(file => file.Path == FabricFile.FileName).Content.ShouldEndWith(
            "modules:\n  reports:\n    module.audit: []\n    module.style: crud\n  billing:\n    module.audit: []\n    module.style: ddd\n");
    }

    [Fact]
    public void A_choice_set_the_catalog_rejects_is_not_regenerated_and_the_catalog_says_why()
    {
        var written = NewThenAdd(Fixtures.MinimalPacks, "minimal", []);
        var record = Record(written.Single(file => file.Path == FabricFile.FileName).Content.Replace(
            "backend.database: postgres",
            "backend.database: oracle",
            StringComparison.Ordinal));

        var regenerated = Generator.Regenerate(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, record);

        regenerated.Files.ShouldBeEmpty();
        regenerated.Errors.ShouldContain(error => error.Contains("'oracle' is not an option of backend.database", StringComparison.Ordinal));
    }

    [Fact]
    public void Spells_out_every_choice_and_setting_for_new_and_every_module_value_for_add_module_in_order()
    {
        var written = NewThenAdd(
            Fixtures.MinimalPacks,
            "minimal",
            ["backend.api-protocols=rest,grpc", "backend.api-protocols.grpc.deadline-seconds=45"],
            "shipping",
            "billing");

        var regeneration = Regeneration.From(Record(written));

        regeneration.Name.ShouldBe("shop");
        regeneration.Profile.ShouldBe("minimal");
        regeneration.Overrides.ShouldBe([
            "backend.database=postgres",
            "backend.api-protocols=rest,grpc",
            "backend.api-protocols.grpc.deadline-seconds=45",
            "module.style=ddd",
        ]);
        regeneration.Modules.Select(module => module.Name).ShouldBe(["shipping", "billing"]);
        regeneration.Modules.Select(module => string.Join(' ', module.Overrides)).ShouldBe(["module.style=ddd", "module.style=ddd"]);
    }
}

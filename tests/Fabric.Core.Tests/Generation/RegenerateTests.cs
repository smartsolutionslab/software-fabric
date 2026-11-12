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

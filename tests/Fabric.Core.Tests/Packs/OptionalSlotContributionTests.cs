using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;
using Fabric.Core.Tests.Generation;

namespace Fabric.Core.Tests.Packs;

/// <summary>A pack.yaml slot contribution with <c>optional</c>: loaded from YAML, composed and rendered like any other.</summary>
public sealed class OptionalSlotContributionTests : IDisposable
{
    private readonly string packsRoot = Directory.CreateTempSubdirectory("fabric-optional-").FullName;

    private static readonly ProjectChoices Choices = new(
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["backend.database"] = ["postgres"],
            ["backend.api-protocols"] = ["rest"],
        },
        new Dictionary<string, IReadOnlyList<string>> { ["module.style"] = ["ddd"] },
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>());

    public OptionalSlotContributionTests()
    {
        var source = Path.Combine(Fixtures.MinimalRoot, "packs");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(packsRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.WriteAllText(copy, File.ReadAllText(file));
        }
    }

    public void Dispose() => Directory.Delete(packsRoot, recursive: true);

    /// <summary>Adds a contribution to the fixture's postgres pack, then loads and composes.</summary>
    private Composition Compose(string slot, bool optional, string content = "## converters")
    {
        var postgres = Path.Combine(packsRoot, "backend", "database", "postgres");
        File.WriteAllText(Path.Combine(postgres, "templates", "extra.sbn"), content);
        var manifest = Path.Combine(postgres, "pack.yaml");
        var entry = $$"""
            slots:
              - { slot: {{slot}}, template: templates/extra.sbn, order: 30, optional: {{(optional ? "true" : "false")}} }

            """;
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("slots:\n", entry, StringComparison.Ordinal));

        var loaded = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog);
        loaded.Errors.ShouldBeEmpty();
        return Composer.Compose(Fixtures.MinimalCatalog, loaded.Packs, Choices);
    }

    private static RenderedProject Render(Composition composition)
    {
        composition.Errors.ShouldBeEmpty();
        return Renderer.Render(Fixtures.MinimalCatalog, composition, Choices, "shop");
    }

    private static string Readme(RenderedProject rendered) =>
        rendered.Files.Single(file => file.Path == "README.md").Content;

    [Fact]
    public void An_optional_contribution_to_a_slot_no_selected_pack_declares_is_dropped()
    {
        var composition = Compose("event-store.json-converters", optional: true);

        composition.Errors.ShouldBeEmpty();
        composition.Slots.ContainsKey("event-store.json-converters").ShouldBeFalse();
        var rendered = Render(composition);
        rendered.Errors.ShouldBeEmpty();
        Readme(rendered).ShouldNotContain("## converters");
    }

    [Fact]
    public void An_optional_contribution_to_a_declared_slot_is_rendered_like_any_other()
    {
        var rendered = Render(Compose("readme.sections", optional: true));

        rendered.Errors.ShouldBeEmpty();
        var readme = Readme(rendered);
        readme.IndexOf("## converters", StringComparison.Ordinal).ShouldBeGreaterThan(readme.IndexOf("## rest", StringComparison.Ordinal));
        readme.IndexOf("## rest", StringComparison.Ordinal).ShouldBeGreaterThan(readme.IndexOf("## postgres", StringComparison.Ordinal));
    }

    [Fact]
    public void A_contribution_that_is_not_optional_to_a_slot_no_selected_pack_declares_is_an_error_naming_the_pack_and_the_slot()
    {
        Compose("event-store.json-converters", optional: false)
            .Errors.ShouldBe(["backend.database.postgres contributes to slot 'event-store.json-converters', which no selected pack declares."]);
    }
}

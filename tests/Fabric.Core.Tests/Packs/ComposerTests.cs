using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Packs;

public class ComposerTests
{
    private static readonly CatalogModel Catalog = CatalogReader.Read(
        new StringReader("""
            version: 1
            dimensions:
              backend.database:
                scope: project
                default: postgres
                options: { postgres: { tier: supported }, sqlserver: { tier: supported } }
              module.style:
                scope: module
                default: ddd
                options: { crud: { tier: supported }, ddd: { tier: supported } }
            """),
        "test.yaml");

    private static PackManifest Pack(
        string option,
        IReadOnlyList<PackFile>? files = null,
        IReadOnlyList<SlotContribution>? slots = null,
        IReadOnlyList<SlotDeclaration>? declares = null) =>
        new(
            option,
            $"/packs/{option.Replace('.', '/')}",
            files ?? [],
            slots ?? [],
            declares ?? [],
            new PackAgents("docs/agents.md", new Dictionary<string, string>()),
            "docs/adr.md",
            [new PackGate(GateKind.UnitTest, "renders", null)]);

    private static Dictionary<string, PackManifest> Packs(params PackManifest[] packs) =>
        packs.ToDictionary(pack => pack.Option);

    private static Dictionary<string, IReadOnlyList<string>> Values(params (string Dimension, string Option)[] values) =>
        values.ToDictionary(value => value.Dimension, value => (IReadOnlyList<string>)[value.Option]);

    private static ProjectChoices Choices(params (string Name, string Style)[] modules) =>
        new(
            Values(("backend.database", "postgres")),
            Values(("module.style", "ddd")),
            modules.ToDictionary(module => module.Name, module => (IReadOnlyDictionary<string, IReadOnlyList<string>>)Values(("module.style", module.Style))));

    private static readonly PackManifest Base = Pack("base.core");
    private static readonly PackManifest Postgres = Pack("backend.database.postgres");
    private static readonly PackManifest Ddd = Pack("module.style.ddd");
    private static readonly PackManifest Crud = Pack("module.style.crud");

    [Fact]
    public void Selects_the_base_packs_and_one_pack_per_chosen_option()
    {
        var composition = Composer.Compose(
            Catalog,
            Packs(Base, Postgres, Pack("backend.database.sqlserver"), Ddd, Crud),
            Choices(("billing", "ddd")));

        composition.Errors.ShouldBeEmpty();
        composition.Packs.Select(pack => pack.Option).ShouldBe(["base.core", "backend.database.postgres", "module.style.ddd"]);
    }

    [Fact]
    public void A_chosen_option_without_a_pack_is_an_error()
    {
        Composer.Compose(Catalog, Packs(Base, Ddd), Choices(("billing", "ddd")))
            .Errors.ShouldBe(["no pack for backend.database=postgres."]);
    }

    [Fact]
    public void Two_packs_claiming_the_same_file_is_an_error()
    {
        var composition = Composer.Compose(
            Catalog,
            Packs(Pack("base.core", files: [new("a.sbn", "README.md", false)]), Pack("backend.database.postgres", files: [new("b.sbn", "README.md", false)]), Ddd),
            Choices());

        composition.Errors.ShouldBe(["README.md is claimed by both base.core and backend.database.postgres."]);
    }

    [Fact]
    public void A_per_module_file_of_a_module_option_renders_only_for_modules_that_chose_it()
    {
        var composition = Composer.Compose(
            Catalog,
            Packs(Base, Postgres, Pack("module.style.ddd", files: [new("domain.sbn", "src/{{ module.name }}/Domain.cs", true)]), Crud),
            Choices(("billing", "ddd"), ("audit", "crud")));

        composition.Errors.ShouldBeEmpty();
        var file = composition.Files.ShouldHaveSingleItem();
        file.Target.ShouldBe("src/billing/Domain.cs");
        file.Module.ShouldBe("billing");
    }

    [Fact]
    public void A_per_module_file_of_a_project_option_renders_for_every_module()
    {
        var composition = Composer.Compose(
            Catalog,
            Packs(Base, Pack("backend.database.postgres", files: [new("db.sbn", "src/{{module.name}}/Database.cs", true)]), Ddd, Crud),
            Choices(("billing", "ddd"), ("audit", "crud")));

        composition.Files.Select(file => file.Target).ShouldBe(["src/billing/Database.cs", "src/audit/Database.cs"]);
    }

    [Fact]
    public void A_per_module_target_without_the_module_name_is_an_error()
    {
        Composer.Compose(Catalog, Packs(Base, Pack("backend.database.postgres", files: [new("db.sbn", "src/Database.cs", true)]), Ddd), Choices())
            .Errors.ShouldBe(["backend.database.postgres: per-module target 'src/Database.cs' must contain {{ module.name }}."]);
    }

    [Fact]
    public void A_project_file_using_the_module_name_is_an_error()
    {
        Composer.Compose(Catalog, Packs(Base, Pack("backend.database.postgres", files: [new("db.sbn", "src/{{ module.name }}.cs", false)]), Ddd), Choices())
            .Errors.ShouldBe(["backend.database.postgres: target 'src/{{ module.name }}.cs' uses {{ module.name }}, but the file is not per-module."]);
    }

    [Fact]
    public void Slot_contributions_are_ordered_by_order_then_option()
    {
        var composition = Composer.Compose(
            Catalog,
            Packs(
                Pack("base.core", declares: [new("readme.sections", "README sections", SlotCardinality.ZeroOrMore)], slots: [new("readme.sections", "base.sbn", 20)]),
                Pack("backend.database.postgres", slots: [new("readme.sections", "pg.sbn", 10)]),
                Pack("module.style.ddd", slots: [new("readme.sections", "ddd.sbn", 10)])),
            Choices());

        composition.Errors.ShouldBeEmpty();
        composition.Slots["readme.sections"].Select(entry => entry.Pack.Option)
            .ShouldBe(["backend.database.postgres", "module.style.ddd", "base.core"]);
    }

    [Fact]
    public void A_declared_slot_without_contributions_is_present_and_empty()
    {
        var composition = Composer.Compose(
            Catalog,
            Packs(Pack("base.core", declares: [new("readme.sections", "README sections", SlotCardinality.ZeroOrMore)]), Postgres, Ddd),
            Choices());

        composition.Slots["readme.sections"].ShouldBeEmpty();
    }

    [Fact]
    public void Contributing_to_an_undeclared_slot_is_an_error()
    {
        Composer.Compose(Catalog, Packs(Base, Pack("backend.database.postgres", slots: [new("nowhere", "x.sbn", 0)]), Ddd), Choices())
            .Errors.ShouldBe(["backend.database.postgres contributes to slot 'nowhere', which no selected pack declares."]);
    }

    [Fact]
    public void Declaring_a_slot_twice_is_an_error()
    {
        Composer.Compose(
            Catalog,
            Packs(
                Pack("base.core", declares: [new("apphost.database", "db", SlotCardinality.ZeroOrMore)]),
                Pack("backend.database.postgres", declares: [new("apphost.database", "db", SlotCardinality.ZeroOrMore)]),
                Ddd),
            Choices())
            .Errors.ShouldBe(["slot 'apphost.database' is declared by both base.core and backend.database.postgres."]);
    }

    [Fact]
    public void An_exactly_one_slot_without_a_contribution_is_an_error()
    {
        Composer.Compose(Catalog, Packs(Pack("base.core", declares: [new("apphost.database", "db", SlotCardinality.ExactlyOne)]), Postgres, Ddd), Choices())
            .Errors.ShouldBe(["slot 'apphost.database' takes exactly one contribution, got 0."]);
    }

    [Theory]
    [InlineData(SlotCardinality.ExactlyOne, "exactly one")]
    [InlineData(SlotCardinality.AtMostOne, "at most one")]
    public void A_single_contribution_slot_with_two_contributions_is_an_error(SlotCardinality cardinality, string wording)
    {
        Composer.Compose(
            Catalog,
            Packs(
                Pack("base.core", declares: [new("apphost.database", "db", cardinality)]),
                Pack("backend.database.postgres", slots: [new("apphost.database", "pg.sbn", 0)]),
                Pack("module.style.ddd", slots: [new("apphost.database", "ddd.sbn", 0)])),
            Choices())
            .Errors.ShouldBe([$"slot 'apphost.database' takes {wording} contribution, got 2: backend.database.postgres, module.style.ddd."]);
    }
}

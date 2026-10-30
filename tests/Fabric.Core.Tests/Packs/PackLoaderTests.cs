using Fabric.Core.Catalog;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Packs;

public sealed class PackLoaderTests : IDisposable
{
    private static readonly CatalogModel Catalog = CatalogReader.Read(
        new StringReader("""
            version: 1
            dimensions:
              backend.database:
                scope: project
                default: postgres
                options: { postgres: { tier: supported }, sqlserver: { tier: supported } }
              frontend.ui-library:
                scope: project
                default: shadcn
                options: { shadcn: { tier: supported } }
            """),
        "test.yaml");

    private readonly PackFixture packs = new();

    public void Dispose() => packs.Dispose();

    private PackLoadResult Load() => PackLoader.Load(packs.Root, Catalog);

    private string SingleError()
    {
        var result = Load();
        return result.Errors.ShouldHaveSingleItem();
    }

    [Fact]
    public void Loads_a_complete_pack()
    {
        packs.AddDefault("backend.database.postgres");

        var result = Load();

        result.Errors.ShouldBeEmpty();
        var pack = result.Packs["backend.database.postgres"];
        pack.Dimension.ShouldBe("backend.database");
        pack.OptionName.ShouldBe("postgres");
        pack.Directory.ShouldBe(Path.Combine(packs.Root, "backend", "database", "postgres"));
        pack.Files.ShouldHaveSingleItem().ShouldBe(new PackFile("templates/readme.md.sbn", "docs/readme.md", PerModule: false));
        pack.Slots.ShouldHaveSingleItem().ShouldBe(new SlotContribution("base.readme.sections", "templates/section.md.sbn", Order: 10));
        pack.Agents.AgentsMd.ShouldBe("docs/agents.md");
        pack.Agents.Briefs["backend-engineer"].ShouldBe("docs/engineer.md");
        pack.Adr.ShouldBe("docs/adr.md");
        pack.Gates.ShouldHaveSingleItem().Kind.ShouldBe(GateKind.UnitTest);
    }

    [Fact]
    public void Packs_are_keyed_by_option_and_ordered_by_it()
    {
        packs.AddDefault("frontend.ui-library.shadcn").AddDefault("backend.database.sqlserver").AddDefault("backend.database.postgres");

        Load().Packs.Keys.ShouldBe(["backend.database.postgres", "backend.database.sqlserver", "frontend.ui-library.shadcn"]);
    }

    [Fact]
    public void Reads_declared_slots_with_their_cardinality()
    {
        packs.Add(
            "base.core",
            PackFixture.DefaultManifest("base.core") + """

                declares-slots:
                  - { slot: base.readme.sections, description: "Sections of the README.", cardinality: zero-or-more }
                  - { slot: apphost.database, description: "The database resource.", cardinality: exactly-one }
                """,
            PackFixture.DefaultFiles);

        var declared = Load().Packs["base.core"].DeclaredSlots;

        declared.Select(slot => slot.Cardinality).ShouldBe([SlotCardinality.ZeroOrMore, SlotCardinality.ExactlyOne]);
    }

    [Fact]
    public void A_base_pack_needs_no_catalog_option()
    {
        packs.AddDefault("base.core");

        Load().Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Rejects_a_pack_without_a_gate()
    {
        packs.Add(
            "backend.database.postgres",
            PackFixture.DefaultManifest("backend.database.postgres").Replace(
                """
                gates:
                  - { kind: unit-test, describes: "The pack renders.", file: templates/gate.cs.sbn }
                """,
                "gates: []",
                StringComparison.Ordinal),
            PackFixture.DefaultFiles);

        SingleError().ShouldStartWith("backend/database/postgres/pack.yaml: /gates:");
    }

    [Fact]
    public void Rejects_a_pack_without_an_adr()
    {
        packs.Add(
            "backend.database.postgres",
            PackFixture.DefaultManifest("backend.database.postgres").Replace("adr: docs/adr.md\n", string.Empty, StringComparison.Ordinal),
            PackFixture.DefaultFiles);

        SingleError().ShouldContain("adr");
    }

    [Fact]
    public void Rejects_an_unknown_field()
    {
        packs.Add(
            "backend.database.postgres",
            PackFixture.DefaultManifest("backend.database.postgres") + "\ncolour: red\n",
            PackFixture.DefaultFiles);

        SingleError().ShouldContain("colour");
    }

    [Fact]
    public void Rejects_a_gate_of_unknown_kind()
    {
        packs.Add(
            "backend.database.postgres",
            PackFixture.DefaultManifest("backend.database.postgres").Replace("kind: unit-test", "kind: vibe-check", StringComparison.Ordinal),
            PackFixture.DefaultFiles);

        SingleError().ShouldStartWith("backend/database/postgres/pack.yaml: /gates/0/kind:");
    }

    [Fact]
    public void Rejects_a_value_of_the_wrong_type()
    {
        packs.Add(
            "backend.database.postgres",
            PackFixture.DefaultManifest("backend.database.postgres").Replace("order: 10", "order: \"10\"", StringComparison.Ordinal),
            PackFixture.DefaultFiles);

        SingleError().ShouldStartWith("backend/database/postgres/pack.yaml: /slots/0/order:");
    }

    [Fact]
    public void Rejects_a_referenced_file_that_does_not_exist()
    {
        packs.Add("backend.database.postgres", PackFixture.DefaultManifest("backend.database.postgres"), [.. PackFixture.DefaultFiles.Where(file => file != "docs/adr.md")]);

        SingleError().ShouldBe("backend/database/postgres/pack.yaml: 'docs/adr.md' does not exist in the pack.");
    }

    [Fact]
    public void Rejects_a_path_that_leaves_the_pack_directory()
    {
        packs.Add(
            "backend.database.postgres",
            PackFixture.DefaultManifest("backend.database.postgres").Replace("adr: docs/adr.md", "adr: ../../shared/adr.md", StringComparison.Ordinal),
            PackFixture.DefaultFiles);

        SingleError().ShouldBe("backend/database/postgres/pack.yaml: '../../shared/adr.md' points outside the pack.");
    }

    [Fact]
    public void Rejects_an_option_that_does_not_match_its_directory()
    {
        packs.AddAt(["backend", "database", "postgres"], PackFixture.DefaultManifest("backend.database.sqlserver"), PackFixture.DefaultFiles);

        SingleError().ShouldBe("backend/database/postgres/pack.yaml: declares option 'backend.database.sqlserver', but its directory says 'backend.database.postgres'.");
    }

    [Fact]
    public void Rejects_a_pack_for_an_option_not_in_the_catalog()
    {
        packs.AddDefault("backend.database.oracle");

        SingleError().ShouldBe("backend/database/oracle/pack.yaml: 'oracle' is not an option of backend.database in the catalog.");
    }

    [Fact]
    public void Reports_every_broken_pack_not_only_the_first()
    {
        packs.AddDefault("backend.database.oracle").AddDefault("frontend.ui-library.mui");

        Load().Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void A_relative_packs_directory_loads_like_an_absolute_one()
    {
        packs.AddDefault("backend.database.postgres");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, packs.Root);

        var result = PackLoader.Load(relative, Catalog);

        result.Errors.ShouldBeEmpty();
        result.Packs["backend.database.postgres"].Directory.ShouldBe(Path.Combine(packs.Root, "backend", "database", "postgres"));
    }
}

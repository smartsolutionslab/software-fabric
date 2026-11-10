using Fabric.Core.Generation;

namespace Fabric.Core.Tests.Generation;

/// <summary>
/// tests/fixtures/minimal shows each rule docs/packs.md describes working, on its packs as checked in: a reader of the
/// page finds a small example of every rule there, and these tests keep each example working.
/// </summary>
public class MinimalFixtureTests
{
    private static GenerationResult New(string profile)
    {
        var result = Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, "shop", profile, []);
        result.Errors.ShouldBeEmpty();
        return result;
    }

    private static string[] Paths(GenerationResult result) => [.. result.Files.Select(file => file.Path)];

    private static string Content(GenerationResult result, string path) =>
        result.Files.Single(file => file.Path == path).Content;

    [Fact]
    public void A_conditional_file_is_written_only_for_the_choices_its_condition_holds_for()
    {
        Content(New("everything"), "docs/protocols.md").ShouldBe("# Protocols\n\nServed: rest, grpc\n");
        Paths(New("minimal")).ShouldNotContain("docs/protocols.md");
    }

    [Fact]
    public void A_pack_brings_its_module_with_its_pinned_choices_over_the_module_defaults()
    {
        var generated = New("everything");

        Content(generated, FabricFile.FileName).ShouldEndWith("modules:\n  archive:\n    module.style: ddd\n");
        Content(generated, "src/archive/README.md").ShouldBe("# archive (ddd)\n");
        Paths(New("minimal")).ShouldNotContain("src/archive/README.md");
    }

    [Fact]
    public void The_pack_that_brings_a_module_writes_that_module_s_own_files_for_it_only()
    {
        var generated = New("everything");
        var project = FabricFile.Read(new StringReader(Content(generated, FabricFile.FileName)), FabricFile.FileName);

        var addition = Generator.AddModule(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, project, "billing", []);

        Content(generated, "src/archive/Archive.txt").ShouldBe("archive of archive on sqlserver\n");
        addition.Errors.ShouldBeEmpty();
        addition.Created.Select(file => file.Path).ShouldBe(["src/billing/README.md"]);
    }

    [Fact]
    public void An_optional_contribution_renders_where_its_slot_is_declared_and_is_dropped_where_it_is_not()
    {
        Content(New("everything"), "config/grpc.txt").ShouldBe("deadline=30s\nservices: rest-gateway\n");
        Paths(New("minimal")).ShouldNotContain("config/grpc.txt");
    }

    [Theory]
    [InlineData("minimal", "provider=postgres\nread by: rest\n")]
    [InlineData("everything", "provider=sqlserver\nread by: grpc, rest\n")]
    public void A_role_slot_is_declared_by_every_option_of_its_dimension_and_filled_whichever_is_chosen(string profile, string database)
    {
        Fixtures.MinimalCatalog.Dimensions["backend.database"].RoleSlots.Keys.ShouldBe(["database.readers"]);
        Content(New(profile), "config/database.txt").ShouldBe(database);
    }

    [Theory]
    [InlineData("minimal", "Orders table: \"orders\"\n")]
    [InlineData("everything", "Orders table: [orders]\n")]
    public void A_template_calls_the_function_the_chosen_pack_defines(string profile, string line)
    {
        Content(New(profile), "README.md").ShouldContain(line, Case.Sensitive);
    }
}

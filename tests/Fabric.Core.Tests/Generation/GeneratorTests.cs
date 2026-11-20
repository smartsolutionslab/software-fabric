using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Generation;

public class GeneratorTests
{
    private static GenerationResult New(string? profile, params string[] overrides) =>
        Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, "shop", profile, overrides);

    private static string Content(GenerationResult result, string path) =>
        result.Files.Single(file => file.Path == path).Content;

    [Fact]
    public void Generates_the_files_of_every_selected_pack_plus_fabric_yaml()
    {
        var result = New("minimal");

        result.Errors.ShouldBeEmpty();
        result.Files.Select(file => file.Path).ShouldBe([
            ".claude/agents/backend-engineer.md",
            "AGENTS.md",
            "README.md",
            "config/database.txt",
            "docs/adr/fabric/backend-api-protocols-rest.md",
            "docs/adr/fabric/backend-database-postgres.md",
            "docs/adr/fabric/base-core.md",
            "docs/adr/fabric/module-style-ddd.md",
            "fabric.yaml",
        ]);
        Content(result, "config/database.txt").ShouldBe("provider=postgres\nread by: rest\n");
        Content(result, "fabric.yaml").ShouldContain("name: shop\nprofile: minimal\n");
    }

    [Fact]
    public void A_root_namespace_given_to_new_is_what_templates_see_and_what_fabric_yaml_records()
    {
        var result = Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, "shop", "minimal", [], "SmartSolutionsLab.Shop");

        result.Errors.ShouldBeEmpty();
        Content(result, ".claude/agents/backend-engineer.md").ShouldStartWith("# Backend engineer (SmartSolutionsLab.Shop)\n");
        Content(result, "fabric.yaml").ShouldContain("name: shop\nnamespace: SmartSolutionsLab.Shop\nprofile: minimal\n");
    }

    [Fact]
    public void Without_a_root_namespace_the_namespace_is_the_project_name_in_pascal_case_and_fabric_yaml_records_none()
    {
        var result = New("minimal");

        Content(result, ".claude/agents/backend-engineer.md").ShouldStartWith("# Backend engineer (Shop)\n");
        Content(result, "fabric.yaml").ShouldNotContain("namespace");
    }

    [Theory]
    [InlineData("")]
    [InlineData("smart-solutions.Shop")]
    [InlineData("Smart Solutions")]
    [InlineData("Acme..Shop")]
    [InlineData(".Acme")]
    [InlineData("Acme.")]
    [InlineData("1Acme.Shop")]
    [InlineData("Acme.2Shop")]
    [InlineData("_Acme")]
    [InlineData("Äcme.Shop")]
    [InlineData("Acme.class")]
    [InlineData("namespace.Shop")]
    public void Refuses_a_root_namespace_that_is_not_a_dotted_csharp_namespace(string rootNamespace)
    {
        var result = Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, "shop", "minimal", [], rootNamespace);

        result.Files.ShouldBeEmpty();
        result.Errors.ShouldBe([
            $"namespace '{rootNamespace}' must be a C# namespace: names of ASCII letters, digits and underscores, each starting with a letter " +
            "and none a C# keyword, separated by dots.",
        ]);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("System.Shop")]
    [InlineData("Microsoft.Shop")]
    public void Refuses_a_root_namespace_under_system_or_microsoft(string rootNamespace)
    {
        var result = Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, "shop", "minimal", [], rootNamespace);

        result.Files.ShouldBeEmpty();
        result.Errors.ShouldBe([
            $"namespace '{rootNamespace}' is under System or Microsoft, which a generated project keeps for .NET's own types and its wiring code.",
        ]);
    }

    [Theory]
    [InlineData("Shop")]
    [InlineData("SmartSolutionsLab.PharmaTraining")]
    [InlineData("acme.shop_2")]
    [InlineData("Acme.Systems.Microsoft")]
    public void Accepts_a_dotted_csharp_namespace(string rootNamespace)
    {
        var result = Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, "shop", "minimal", [], rootNamespace);

        result.Errors.ShouldBeEmpty();
        Content(result, "fabric.yaml").ShouldContain($"\nnamespace: {rootNamespace}\n");
    }

    [Fact]
    public void Fills_slots_from_every_selected_pack_in_order()
    {
        Content(New("everything"), "README.md").ShouldBe("""
            # Generated project

            Database: sqlserver
            Protocols: rest, grpc
            Modules start as: crud
            Orders table: [orders]

            ## sqlserver

            ## grpc

            ## rest

            ## modules: crud

            ## modules: ddd


            """);
    }

    [Fact]
    public void A_rule_violation_stops_generation()
    {
        var result = New("minimal", "backend.api-protocols=grpc");

        result.Files.ShouldBeEmpty();
        result.Errors.ShouldBe(["backend.api-protocols=grpc requires backend.api-protocols has rest, but backend.api-protocols is grpc."]);
    }

    [Fact]
    public void A_resolution_error_stops_generation()
    {
        New("nonexistent").Errors.ShouldBe(["unknown profile 'nonexistent'."]);
    }

    [Fact]
    public void A_chosen_option_without_a_pack_stops_generation()
    {
        var packs = Fixtures.MinimalPacks.Where(pack => pack.Key != "backend.database.postgres").ToDictionary();

        var result = Generator.New(Fixtures.MinimalCatalog, packs, "shop", "minimal", []);

        result.Files.ShouldBeEmpty();
        result.Errors[0].ShouldBe("no pack for backend.database=postgres.");
    }

    [Fact]
    public void A_pack_may_not_write_fabric_yaml()
    {
        var packs = Fixtures.MinimalPacks.ToDictionary();
        var postgres = packs["backend.database.postgres"];
        packs["backend.database.postgres"] = postgres with { Files = [new PackFile("templates/connection.sbn", "fabric.yaml", PerModule: false)] };

        Generator.New(Fixtures.MinimalCatalog, packs, "shop", "minimal", [])
            .Errors.ShouldBe(["backend.database.postgres claims fabric.yaml, which the generator writes."]);
    }

    [Fact]
    public void Generating_twice_gives_identical_output()
    {
        New("everything").Files.ShouldBe(New("everything").Files);
    }

    [Fact]
    public void Every_selected_pack_contributes_its_agents_md_to_agents_md_in_selection_order()
    {
        Content(New("minimal"), "AGENTS.md").ShouldBe("""
            # Agent guide for shop

            Agent rules for base.core.

            Agent rules for backend.database.postgres.

            Agent rules for backend.api-protocols.rest.

            Agent rules for module.style.ddd.

            """);
    }

    [Fact]
    public void A_role_brief_collects_the_briefs_packs_give_for_that_role_rendered_as_templates()
    {
        Content(New("minimal"), ".claude/agents/backend-engineer.md").ShouldBe("# Backend engineer (Shop)\n\nWrite EF Core migrations for postgres.\n");
    }

    [Fact]
    public void Every_selected_pack_gets_its_adr_named_after_its_option_under_docs_adr_fabric()
    {
        Content(New("minimal"), "docs/adr/fabric/backend-database-postgres.md").ShouldBe("# ADR: backend.database.postgres\n");
    }

    [Theory]
    [InlineData("Shop")]
    [InlineData("my_shop")]
    [InlineData("")]
    public void Rejects_a_project_name_that_is_not_lower_kebab_case(string name)
    {
        Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, name, "minimal", [])
            .Errors.ShouldBe([$"project name '{name}' must be lower-case letters, digits and hyphens, starting with a letter."]);
    }
}

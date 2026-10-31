using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Generation;

public class GeneratorTests
{
    private static GenerationResult New(string? profile, params string[] overrides) =>
        Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, profile, overrides);

    private static string Content(GenerationResult result, string path) =>
        result.Files.Single(file => file.Path == path).Content;

    [Fact]
    public void Generates_the_files_of_every_selected_pack_plus_fabric_yaml()
    {
        var result = New("minimal");

        result.Errors.ShouldBeEmpty();
        result.Files.Select(file => file.Path).ShouldBe(["README.md", "config/database.txt", "fabric.yaml"]);
        Content(result, "config/database.txt").ShouldBe("provider=postgres\n");
        Content(result, "fabric.yaml").ShouldContain("profile: minimal\n");
    }

    [Fact]
    public void Fills_slots_from_every_selected_pack_in_order()
    {
        Content(New("everything"), "README.md").ShouldBe("""
            # Generated project

            Database: sqlserver
            Protocols: rest, grpc
            Modules start as: crud

            ## sqlserver

            ## grpc

            ## rest

            ## modules: crud


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

        var result = Generator.New(Fixtures.MinimalCatalog, packs, "minimal", []);

        result.Files.ShouldBeEmpty();
        result.Errors[0].ShouldBe("no pack for backend.database=postgres.");
    }

    [Fact]
    public void A_pack_may_not_write_fabric_yaml()
    {
        var packs = Fixtures.MinimalPacks.ToDictionary();
        var postgres = packs["backend.database.postgres"];
        packs["backend.database.postgres"] = postgres with { Files = [new PackFile("templates/connection.sbn", "fabric.yaml", PerModule: false)] };

        Generator.New(Fixtures.MinimalCatalog, packs, "minimal", [])
            .Errors.ShouldBe(["backend.database.postgres claims fabric.yaml, which the generator writes."]);
    }

    [Fact]
    public void Generating_twice_gives_identical_output()
    {
        New("everything").Files.ShouldBe(New("everything").Files);
    }
}

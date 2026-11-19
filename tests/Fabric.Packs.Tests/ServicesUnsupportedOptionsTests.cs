using System.Text.Json;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017) refuses what it cannot build yet, so the matrix never picks it: its
/// integration tests run through the AppHost (<c>aspire-fixture</c>), never <c>testcontainers</c> (owner, #723), and
/// the singletons #707 moves to the edge (SignalR, SSE and AG-UI) conflict with it until their sub-issues of #707 lift
/// them. The MCP server is no longer one (#766): each service is its own. A modular monolith keeps every one of these
/// options.
/// </summary>
public sealed class ServicesUnsupportedOptionsTests
{
    private const string Profile = "saas-standard-react";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none"];

    private static readonly string[] Agents = ["backend.llm=openai", "backend.llm.agents=agent-framework"];

    public static TheoryData<string[], string> Refused { get; } = new()
    {
        { ["backend.test.integration=testcontainers"], "backend.test.integration=testcontainers conflicts with backend.topology == services." },
        { ["frontend.realtime=signalr"], "frontend.realtime=signalr conflicts with backend.topology == services." },
        { ["frontend.realtime=sse"], "frontend.realtime=sse conflicts with backend.topology == services." },
        { [.. Agents, "frontend.agent-ui=ag-ui"], "frontend.agent-ui=ag-ui conflicts with backend.topology == services." },
    };

    public static TheoryData<string[]> KeptByTheMonolith { get; } = new()
    {
        { ["backend.test.integration=testcontainers"] },
        { ["backend.test.integration=aspire-fixture"] },
        { ["backend.mcp=server"] },
        { ["frontend.realtime=signalr"] },
        { ["frontend.realtime=sse"] },
        { [.. Agents, "frontend.agent-ui=ag-ui"] },
    };

    [Fact]
    public void With_services_the_integration_tests_run_through_the_aspire_fixture_when_nothing_is_chosen()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, Services);

        generated.Errors.ShouldBeEmpty();
        generated.Files.Single(file => file.Path == FabricFile.FileName).Content.ShouldContain("backend.test.integration: aspire-fixture");
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void With_services_an_option_it_cannot_build_yet_is_refused_naming_the_conflict(string[] choices, string error)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [.. Services, .. choices]);

        generated.Errors.ShouldContain(error);
    }

    [Theory]
    [MemberData(nameof(KeptByTheMonolith))]
    public void A_modular_monolith_keeps_the_option(string[] choices)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, ["backend.tenancy=none", .. choices]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_reason_is_recorded_in_adr_0017()
    {
        var adr = File.ReadAllText(Path.Combine(Repository.Root, "docs", "adr", "0017-services-topology.md"));

        adr.ShouldContain("backend.test.integration == testcontainers", customMessage: "ADR-0017 records why services refuse testcontainers.");
        adr.ShouldContain("#707", customMessage: "ADR-0017 says #707 lifts the singletons' conflicts.");
    }

    [Fact]
    public void No_services_combination_of_the_matrix_picks_an_option_services_refuse()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "build", "matrix.json")));
        var services = matrix.RootElement.GetProperty("combinations").EnumerateArray()
            .Where(combination => Choice(combination, "backend.topology").Contains("services"))
            .ToList();

        services.ShouldNotBeEmpty();
        foreach (var combination in services)
        {
            var name = combination.GetProperty("name").GetString();
            Choice(combination, "backend.test.integration").ShouldBe(["aspire-fixture"], name);
            Choice(combination, "frontend.realtime").ShouldBe(["none"], name);
            Choice(combination, "frontend.agent-ui").ShouldNotContain("ag-ui", name);
        }
    }

    private static List<string> Choice(JsonElement combination, string dimension)
    {
        if (!combination.GetProperty("choices").TryGetProperty(dimension, out var value))
        {
            return [];
        }

        return value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(option => option.GetString()!)]
            : [value.GetString()!];
    }
}

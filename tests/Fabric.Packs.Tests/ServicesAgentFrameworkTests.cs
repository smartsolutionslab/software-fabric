using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017) with <c>backend.llm.agents: agent-framework</c> (ADR-0011): the rule that
/// every agent tool's request is sent by an endpoint marked <c>.Sends&lt;TRequest&gt;()</c> starts every service host,
/// as the endpoint rules do, and requires that endpoint in the service host of the request's own module; a violation
/// names the request and that service host. No test here calls a model.
/// </summary>
public sealed class ServicesAgentFrameworkTests
{
    private const string Profile = "saas-standard-react";
    private const string Rule = "tests/Architecture.Tests/Rules/EveryAgentToolRequestHasAnEndpoint.cs";
    private const string Request = "Shop.Modules.Billing.Application.Queries.AskTheModel.AskTheModel";

    private const string AgentInApplication = """
        using Shop.BuildingBlocks.Agents;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Billing.Application.Queries.AskTheModel;
        using Microsoft.Agents.AI;
        using Microsoft.Extensions.AI;

        namespace Shop.Modules.Billing.Application.Agents;

        public static class BillingAgent
        {
            public static AIAgent Create(IChatClient model, IDispatcher dispatcher) =>
                model.AsAIAgent(
                    instructions: "Answer questions about invoices with the tools.",
                    name: "billing",
                    tools: [DispatcherTool.For<AskTheModel>(dispatcher, "Asks the model a question.")]);
        }
        """;

    private const string EndpointSendingTheToolsRequest = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Billing.Application.Queries.AskTheModel;

        namespace Shop.Modules.Billing.Api.Endpoints;

        public sealed class Ask : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/ask", async (string question, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                    (await dispatcher.QueryAsync(new AskTheModel(question), cancellationToken)).ToHttpResult())
                    .Sends<AskTheModel>();
        }
        """;

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none", "backend.llm=ollama", "backend.llm.agents=agent-framework"];

    [Fact]
    public void A_services_project_with_agents_generates_from_the_real_packs_alone()
    {
        Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, Services).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void With_services_the_endpoint_rule_starts_every_service_host_instead_of_failing_the_build()
    {
        var rule = Files(Services)[Rule];

        rule.ShouldNotContain("#error");
        rule.ShouldContain("ServiceHostEntryPoints.All");
        rule.ShouldContain("MappedHost<TEntryPoint>");
        rule.ShouldContain("src/Services/", customMessage: "a violation names the service host of the request's module.");
        rule.ShouldNotContain("WebApplicationFactory<Program>", customMessage: "with services there is no single host's Program.");
    }

    [Fact]
    public void With_a_modular_monolith_the_endpoint_rule_still_starts_the_one_host()
    {
        var rule = Files(["backend.tenancy=none", "backend.llm=ollama", "backend.llm.agents=agent-framework"])[Rule];

        rule.ShouldNotContain("#error");
        rule.ShouldContain("WebApplicationFactory<Program>");
        rule.ShouldNotContain("ServiceHostEntryPoints");
    }

    [Fact]
    public void A_tool_request_sent_by_an_endpoint_in_its_own_service_host_passes_the_architecture_tests()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.AddModule("shipping");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Agents", AgentInApplication);
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", EndpointSendingTheToolsRequest);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_tool_request_no_endpoint_of_its_service_host_sends_fails_the_architecture_tests_naming_the_request_and_the_service_host()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.AddModule("shipping");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Agents", AgentInApplication);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.Split('\n')
            .ShouldContain(line => line.Contains(Request, StringComparison.Ordinal) && line.Contains("src/Services/Billing/Host", StringComparison.Ordinal), output);
        // The started hosts log their content root, so only a violation naming Shipping's service host counts.
        output.Split('\n')
            .ShouldNotContain(line => line.Contains("is an agent's tool", StringComparison.Ordinal) && line.Contains("src/Services/Shipping/Host", StringComparison.Ordinal), output);
        output.ShouldNotContain("#error");
    }

    private static Dictionary<string, string> Files(string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
    }

    /// <summary>A services project with agents; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject()
    {
        var project = new GeneratedProject(Profile, Services);
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr", "fabric"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count != 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty(string.Join(", ", standIns));
        return project;
    }
}

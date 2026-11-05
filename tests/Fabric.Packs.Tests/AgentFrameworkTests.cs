using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm.agents: agent-framework</c> (ADR-0011): agents with Microsoft Agent Framework on the project's one
/// <c>IChatClient</c>. An agent lives in a module's Application project, is run by a command or query, and calls the
/// module's use cases as tools through <c>IDispatcher</c>; MCP client tools can be given to it too. No test here calls a
/// model: the generated tests use a stand-in that asks for a tool call.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class AgentFrameworkTests
{
    private const string Profile = "lean-crud";
    private const string Anthropic = "backend.llm=anthropic";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string Sdk = "<PackageVersion Include=\"Microsoft.Agents.AI\" Version=\"1.26.0\" />";

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

    private const string EndpointRunningAnAgent = """
        using Microsoft.Agents.AI;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.AI;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api.Endpoints;

        public sealed class AskAnAgent : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/ask", async (string question, IChatClient model, CancellationToken cancellationToken) =>
                    TypedResults.Ok((await model.AsAIAgent(instructions: "Answer.").RunAsync(question, cancellationToken: cancellationToken)).Text));
        }
        """;

    private const string ToolBypassingTheDispatcher = """
        using Microsoft.Agents.AI;
        using Microsoft.Extensions.AI;

        namespace Shop.Modules.Billing.Application.Agents;

        public static class ShortcutAgent
        {
            public static AIAgent Create(IChatClient model) =>
                model.AsAIAgent(
                    instructions: "Count the invoices.",
                    tools: [AIFunctionFactory.Create(() => 42, "count_invoices", "Counts the invoices.")]);
        }
        """;

    private const string AgentSelectingMcpTools = """
        using Shop.BuildingBlocks.Agents;
        using Shop.BuildingBlocks.Mcp;
        using Microsoft.Agents.AI;
        using Microsoft.Extensions.AI;

        namespace Shop.Modules.Billing.Application.Agents;

        public static class DocsAgent
        {
            public static async Task<AIAgent> CreateAsync(IChatClient model, IAgentMcpTools mcpTools, CancellationToken cancellationToken)
            {
                var tools = await mcpTools.ToolsAsync(AllowSearch, cancellationToken);
                var first = tools[0];
                var search = tools.Single(tool => tool.Name == "docs__search");
                return model.AsAIAgent(instructions: "Answer from the docs.", tools: [first, search]);
            }

            private static bool AllowSearch(McpToolCall call) => call.Tool == "search";
        }
        """;

    private const string AgentWithoutTheChoice = """
        using Microsoft.Agents.AI;
        using Microsoft.Extensions.AI;

        namespace Shop.Modules.Billing.Application;

        public static class HandWiredAgent
        {
            public static AIAgent Create(IChatClient model) => model.AsAIAgent(instructions: "Answer.");
        }
        """;

    private const string ApplicationReferencingTheFramework = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Microsoft.Agents.AI" VersionOverride="1.26.0" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void A_project_with_agents_generates_from_the_real_packs_alone()
    {
        Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Anthropic, AgentFramework]).Errors.ShouldBeEmpty();
        Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Anthropic, AgentFramework, "backend.mcp=client"]).Errors.ShouldBeEmpty();
        Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Anthropic]).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Application_projects_reference_the_framework_and_the_tools_go_through_the_dispatcher()
    {
        var files = Files(Profile, Anthropic, AgentFramework);

        Count(files["Directory.Packages.props"], Sdk).ShouldBe(1);
        files["src/Modules/Directory.Build.props"].ShouldContain("<PackageReference Include=\"Microsoft.Agents.AI\" />");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldNotContain("Microsoft.Agents.AI");
        files["src/BuildingBlocks/Agents/DispatcherTool.cs"].ShouldContain("IDispatcher");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/AgentsLiveInApplication.cs");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/AgentToolsGoThroughTheDispatcher.cs");
        files.Keys.ShouldNotContain("tests/Architecture.Tests/Rules/NoAgentFramework.cs");
        files["Solution.slnx"].ShouldContain("tests/Agents.Tests/Agents.Tests.csproj");
        files.Keys.ShouldNotContain("src/BuildingBlocks/Agents/IAgentMcpTools.cs");
    }

    [Fact]
    public void A_project_that_calls_a_model_without_agents_gets_the_rule_against_the_framework_and_no_package()
    {
        var files = Files(Profile, Anthropic);

        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/NoAgentFramework.cs");
        files["Directory.Packages.props"].ShouldNotContain("Microsoft.Agents.AI");
        files.Keys.ShouldNotContain(path => path.StartsWith("tests/Agents.Tests/", StringComparison.Ordinal));
    }

    [Fact]
    public void With_the_mcp_client_an_agent_can_be_given_the_declared_servers_tools()
    {
        var files = Files(Profile, Anthropic, AgentFramework, "backend.mcp=client");

        files["src/BuildingBlocks/Agents/IAgentMcpTools.cs"].ShouldContain("interface IAgentMcpTools");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddAgentMcpTools();");
        files["tests/Agents.Tests/Agents.Tests.csproj"].ShouldContain("Mcp.TestServer.csproj");
    }

    [Fact]
    public void No_profile_changes_because_none_calls_a_model()
    {
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var files = Files(profile);
            files.Keys.ShouldNotContain(path => path.Contains("Agent", StringComparison.Ordinal), profile);
            files["fabric.yaml"].ShouldNotContain("backend.llm.agents", customMessage: profile);
        }
    }

    [Fact]
    public void The_generated_agent_tests_pass_and_an_agent_in_application_passes_the_architecture_rules()
    {
        using var project = NewProject(Anthropic, AgentFramework);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Agents", AgentInApplication);
        project.Build();

        var (agentsExitCode, agentsOutput) = project.Test("tests/Agents.Tests/Agents.Tests.csproj");
        agentsExitCode.ShouldBe(0, agentsOutput);
        agentsOutput.ShouldContain("failed: 0");
        agentsOutput.ShouldContain("succeeded: 8");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void With_the_mcp_client_the_generated_agent_tests_give_an_agent_a_declared_servers_tool()
    {
        using var project = NewProject(Anthropic, AgentFramework, "backend.mcp=client");
        project.Build();

        var (agentsExitCode, agentsOutput) = project.Test("tests/Agents.Tests/Agents.Tests.csproj");
        agentsExitCode.ShouldBe(0, agentsOutput);
        agentsOutput.ShouldContain("failed: 0");
        agentsOutput.ShouldContain("succeeded: 9");

        var (mcpExitCode, mcpOutput) = project.Test("tests/Mcp.Tests/Mcp.Tests.csproj");
        mcpExitCode.ShouldBe(0, mcpOutput);
        mcpOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void With_nullable_absence_and_minimal_results_the_generated_agent_tests_pass()
    {
        // The BCL's guards: minimal's own Result does not compile with Ensure.That's (on main too), which is not this pack's.
        using var project = NewProject(Anthropic, AgentFramework, "backend.absence=nullable", "backend.result-style=minimal", "backend.guards=bcl");
        project.Build();

        var (exitCode, output) = project.Test("tests/Agents.Tests/Agents.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 5");
    }

    [Fact]
    public void An_endpoint_running_an_agent_fails_the_architecture_tests_naming_its_assembly()
    {
        var output = ArchitectureFailure("src/Modules/Billing/Billing.Api/Endpoints", EndpointRunningAnAgent);

        output.ShouldContain("Shop.Modules.Billing.Api references Microsoft.Agents.AI, but an agent lives in a module's Application project");
    }

    [Fact]
    public void A_tool_that_bypasses_the_dispatcher_fails_the_architecture_tests_naming_the_agent()
    {
        var output = ArchitectureFailure("src/Modules/Billing/Billing.Application/Agents", ToolBypassingTheDispatcher);

        output.ShouldContain("Shop.Modules.Billing.Application.Agents.ShortcutAgent makes a tool with AIFunctionFactory.Create, not DispatcherTool.For");
    }

    [Fact]
    public void An_agent_selecting_mcp_tools_by_index_and_by_single_passes_the_architecture_rules()
    {
        using var project = NewProject(Anthropic, AgentFramework, "backend.mcp=client");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Agents", AgentSelectingMcpTools);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void Without_agents_a_module_referencing_the_framework_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Anthropic);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Billing.Application.csproj", ApplicationReferencingTheFramework);
        project.WriteTypes("src/Modules/Billing/Billing.Application", AgentWithoutTheChoice);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application references Microsoft.Agents.AI, but this project has no agents (backend.llm.agents: none)");
    }

    /// <summary>The architecture tests' output for a project with agents whose billing module holds the source.</summary>
    private static string ArchitectureFailure(string directory, string source)
    {
        using var project = NewProject(Anthropic, AgentFramework);
        project.AddModule("billing");
        project.WriteTypes(directory, source);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        return output;
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static int Count(string text, string line) =>
        text.Split('\n').Count(candidate => candidate.Trim() == line);

    /// <summary>A lean-crud project with the overrides; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(params string[] overrides)
    {
        var project = new GeneratedProject(Profile, overrides);
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count > 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty();
        return project;
    }
}

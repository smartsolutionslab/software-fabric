using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.dashboards: [user-editable, generative]</c> (ADR-0015, #185), its backend: the dashboards module gets an
/// agent, <c>dashboards</c>, reached through the agent panel's <c>POST /agents/{name}</c>. Its tools are the module's
/// <c>ListWidgets</c> and <c>ProposeWidget</c>, both queries: it proposes a declared widget as a fitting visual and saves
/// nothing, and the open draft reaches the model as context from the AG-UI state.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class GenerativeDashboardsTests
{
    private const string Profile = "saas-standard-react";
    private const string Anthropic = "backend.llm=anthropic";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string AgUi = "frontend.agent-ui=ag-ui";
    private const string Generative = "frontend.dashboards=user-editable,generative";
    private const string Application = "src/Modules/Dashboards/Dashboards.Application";
    private const string Rule = "tests/Architecture.Tests/Rules/TheDashboardAgentSavesNothing.cs";

    private const string AgentSavingADashboard = """
        using Microsoft.Agents.AI;
        using Microsoft.Extensions.AI;
        using Shop.BuildingBlocks.Agents;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Dashboards.Application.Commands.SaveDashboard;

        namespace Shop.Modules.Dashboards.Application.Agents;

        public static class SavingAgent
        {
            public static AIAgent Create(IChatClient model, IDispatcher dispatcher) =>
                model.AsAIAgent(instructions: "Save the dashboard.", tools: [DispatcherTool.For<SaveDashboard>(dispatcher, "Saves a dashboard.")]);
        }
        """;

    [Fact]
    public void A_project_with_generative_dashboards_generates_from_the_real_packs_alone()
    {
        foreach (var (profile, overrides) in new[]
        {
            (Profile, new[] { Anthropic, AgentFramework, AgUi, Generative }),
            ("lean-crud", [Anthropic, AgentFramework, AgUi, Generative, "backend.absence=nullable", "backend.result-style=minimal", "backend.guards=bcl"]),
            ("saas-standard-vue", [Anthropic, AgentFramework, AgUi, Generative]),
        })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides).Errors.ShouldBeEmpty(profile);
        }
    }

    [Fact]
    public void The_dashboards_module_gets_an_agent_whose_tools_are_the_widget_catalogue_and_a_proposal()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi, Generative);

        var agent = files[$"{Application}/Agents/DashboardAgent.cs"];
        agent.ShouldContain("DispatcherTool.For<ListWidgets>(dispatcher,");
        agent.ShouldContain("DispatcherTool.For<ProposeWidget>(dispatcher,");
        files[$"{Application}/Queries/TalkToTheDashboards/TalkToTheDashboards.cs"].ShouldContain("IAgentConversation<TalkToTheDashboards>");
        files[$"{Application}/Queries/TalkToTheDashboards/TalkToTheDashboards.cs"].ShouldContain("public static string AgentName => \"dashboards\";");
        files[$"{Application}/Queries/ProposeWidget/ProposeWidget.cs"].ShouldContain("IQuery<Result<WidgetProposalDto, ApiError>>");
        files[Rule].ShouldContain("class TheDashboardAgentSavesNothing : IArchitectureRule");
        files.Keys.ShouldContain("tests/Integration.Tests/GenerativeDashboardsTests.cs");
        files.Keys.ShouldContain("tests/Modules/Dashboards.Tests/Queries/TalkToTheDashboardsTests.cs");
        files["AGENTS.md"].ShouldContain("## Generative dashboards");
    }

    [Fact]
    public void Without_generative_dashboards_the_dashboards_module_has_no_agent()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi, "frontend.dashboards=user-editable");

        files.Keys.ShouldNotContain(path => path.Contains("DashboardAgent", StringComparison.Ordinal) || path.Contains("TalkToTheDashboards", StringComparison.Ordinal) || path.Contains("ProposeWidget", StringComparison.Ordinal));
        files.Keys.ShouldNotContain(Rule);
    }

    [Fact]
    public void No_profile_changes_because_none_has_generative_dashboards()
    {
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var files = Files(profile);

            files.Keys.ShouldNotContain(path => path.Contains("DashboardAgent", StringComparison.Ordinal) || path.Contains("Generative", StringComparison.Ordinal), profile);
            files["fabric.yaml"].ShouldNotContain("generative", customMessage: profile);
        }
    }

    [Fact]
    public void Generative_dashboards_need_the_agent_panel()
    {
        Validate(Anthropic, AgentFramework, Generative).ShouldContain("frontend.dashboards=generative requires frontend.agent-ui == ag-ui, but frontend.agent-ui is none.");
        Validate(Anthropic, AgentFramework, AgUi, Generative).ShouldBeEmpty();
    }

    [Fact]
    public void The_untouched_project_builds_and_its_dashboards_and_architecture_tests_pass()
    {
        using var project = NewProject(Profile, Anthropic, AgentFramework, AgUi, Generative);
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Dashboards.Tests/Dashboards.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        // The module's own test and the sample model test, then the agent's 8 and the proposal's 9.
        moduleOutput.ShouldContain("succeeded: 19");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void With_minimal_results_bcl_guards_and_nullable_absence_the_project_builds_and_its_dashboards_tests_pass()
    {
        using var project = NewProject("lean-crud", Anthropic, AgentFramework, AgUi, Generative, "backend.absence=nullable", "backend.result-style=minimal", "backend.guards=bcl");
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Dashboards.Tests/Dashboards.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 19");
    }

    [Fact]
    public void A_command_given_to_an_agent_of_the_dashboards_module_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Profile, Anthropic, AgentFramework, AgUi, Generative);
        project.WriteTypes($"{Application}/Agents", AgentSavingADashboard);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("failed Shop.Architecture.Tests.ArchitectureRulesTests.Rule_holds(rule: \"TheDashboardAgentSavesNothing\")", customMessage: output);
        output.ShouldContain(
            "Shop.Modules.Dashboards.Application.Agents.SavingAgent gives an agent the command Shop.Modules.Dashboards.Application.Commands.SaveDashboard.SaveDashboard as a tool",
            customMessage: output);
    }

    [Fact]
    public void The_integration_tests_propose_a_declared_widget_refuse_an_undeclared_one_and_save_nothing()
    {
        using var project = NewProject(Profile, Anthropic, AgentFramework, AgUi, Generative);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        // Before the exit code: the copy failing is a failed test too, and this names the cause.
        output.ShouldNotContain("The_frontends_copy_of_the_openapi_document", customMessage: output);
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("skipped: 1");

        // Passing tests are not named in the output: the class alone shows that its six ran.
        var (classExitCode, classOutput) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-class", "Shop.Integration.Tests.GenerativeDashboardsTests");
        classExitCode.ShouldBe(0, classOutput);
        classOutput.ShouldContain("succeeded: 6", customMessage: classOutput);
    }

    private static List<string> Validate(params string[] overrides)
    {
        var resolution = ChoiceResolver.Resolve(Repository.Catalog, Profile, overrides);
        return resolution.Choices is { } choices
            ? [.. RuleEvaluator.Check(Repository.Catalog, choices)]
            : [.. resolution.Errors];
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A project with the overrides; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string profile, params string[] overrides)
    {
        var project = new GeneratedProject(profile, overrides);
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

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
    private const string Vue = "saas-standard-vue";
    private const string Anthropic = "backend.llm=anthropic";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string AgUi = "frontend.agent-ui=ag-ui";
    private const string Generative = "frontend.dashboards=user-editable,generative";
    private const string Application = "src/Modules/Dashboards/Dashboards.Application";
    private const string Rule = "tests/Architecture.Tests/Rules/TheDashboardAgentSavesNothing.cs";
    private const string Page = "web/app/src/dashboards";
    private const string EndToEndTest = "web/app/e2e/generative-dashboards.spec.ts";

    /// <summary>The Vue page's pending widget, wrapped so that Accept saves at once instead of only changing the draft.</summary>
    private const string VuePendingWidgetSavingAtOnce = """
        <script setup lang="ts">
        import { usePutDashboardsId } from '@/api/generated';
        import Accepting from './AcceptingPendingWidget.vue';

        const emit = defineEmits<{ accept: []; discard: [] }>();
        const { mutateAsync: save } = usePutDashboardsId();

        const accept = () => {
          void save({ id: 'saved-at-once', data: { name: 'At once', widgets: [] } });
          emit('accept');
        };
        </script>

        <template>
          <Accepting v-bind="$attrs" @accept="accept" @discard="emit('discard')" />
        </template>
        """;

    /// <summary>The page's pending widget, wrapped so that Accept saves at once instead of only changing the draft.</summary>
    private const string PendingWidgetSavingAtOnce = """
        import type { ComponentProps } from 'react';
        import { usePutDashboardsByIdMutation } from '@/api/generated';
        import { PendingWidget as Accepting } from './AcceptingPendingWidget';

        export function PendingWidget(props: ComponentProps<typeof Accepting>) {
          const [save] = usePutDashboardsByIdMutation();
          const onAccept = () => {
            void save({ id: 'saved-at-once', saveDashboardRequest: { name: 'At once', widgets: [] } });
            props.onAccept();
          };
          return <Accepting {...props} onAccept={onAccept} />;
        }
        """;

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

    [Fact]
    public void The_react_dashboard_page_gets_an_assistant_whose_proposals_the_canvas_shows_pending()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi, Generative);

        files[$"{Page}/DashboardCanvas.tsx"].ShouldContain("<DashboardAssistant ");
        files[$"{Page}/DashboardCanvas.tsx"].ShouldContain("<PendingWidget");
        files[$"{Page}/DashboardAssistant.tsx"].ShouldContain("useAgent('dashboards', ");
        files.Keys.ShouldContain($"{Page}/PendingWidget.tsx");
        files.Keys.ShouldContain($"{Page}/proposal.ts");
        files.Keys.ShouldContain($"{Page}/DashboardAssistant.test.tsx");
        files.Keys.ShouldContain($"{Page}/proposal.test.ts");
        files.Keys.ShouldContain(EndToEndTest);
        files["web/app/src/agents/useAgent.ts"].ShouldContain("export function useAgent(name: string, state?: () => unknown)");
        files["AGENTS.md"].ShouldContain("`DashboardAssistant`");
    }

    [Fact]
    public void Without_generative_dashboards_the_dashboard_page_has_no_assistant()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi, "frontend.dashboards=user-editable");

        files[$"{Page}/DashboardCanvas.tsx"].ShouldNotContain("DashboardAssistant");
        files[$"{Page}/DashboardCanvas.tsx"].ShouldNotContain("PendingWidget");
        files.Keys.ShouldNotContain(path => path.Contains("DashboardAssistant", StringComparison.Ordinal) || path.Contains("PendingWidget", StringComparison.Ordinal) || path.EndsWith("/proposal.ts", StringComparison.Ordinal));
        files.Keys.ShouldNotContain(EndToEndTest);
    }

    [Fact]
    public void The_vue_dashboard_page_gets_the_same_assistant_with_the_shared_proposal_and_end_to_end_test()
    {
        var files = Files(Vue, Anthropic, AgentFramework, AgUi, Generative);

        files[$"{Page}/DashboardCanvas.vue"].ShouldContain("<DashboardAssistant ");
        files[$"{Page}/DashboardCanvas.vue"].ShouldContain("<PendingWidget");
        files[$"{Page}/DashboardAssistant.vue"].ShouldContain("useAgent('dashboards', ");
        files.Keys.ShouldContain($"{Page}/PendingWidget.vue");
        files.Keys.ShouldContain($"{Page}/DashboardAssistant.test.ts");
        files[$"{Page}/proposal.ts"].ShouldBe(Files(Profile, Anthropic, AgentFramework, AgUi, Generative)[$"{Page}/proposal.ts"]);
        files.Keys.ShouldContain($"{Page}/proposal.test.ts");
        files[EndToEndTest].ShouldContain("page.locator('.vgl-item:not(.vgl-item--placeholder)')");
        files.Keys.ShouldNotContain(path => path.EndsWith(".tsx", StringComparison.Ordinal));
        files["web/app/src/agents/useAgent.ts"].ShouldContain("export function useAgent(name: string, state?: () => unknown)");
        files["AGENTS.md"].ShouldContain("`DashboardAssistant.vue`");
    }

    [Fact]
    public void Without_generative_dashboards_the_vue_dashboard_page_has_no_assistant()
    {
        var files = Files(Vue, Anthropic, AgentFramework, AgUi, "frontend.dashboards=user-editable");

        files[$"{Page}/DashboardCanvas.vue"].ShouldNotContain("DashboardAssistant");
        files[$"{Page}/DashboardCanvas.vue"].ShouldNotContain("PendingWidget");
        files.Keys.ShouldNotContain(path => path.Contains("DashboardAssistant", StringComparison.Ordinal) || path.Contains("PendingWidget", StringComparison.Ordinal) || path.Contains("proposal.", StringComparison.Ordinal));
        files.Keys.ShouldNotContain(EndToEndTest);
    }

    [Fact]
    public void With_another_vue_ui_library_the_assistant_fails_saying_it_is_built_on_shadcn_vue_only()
    {
        var files = Files(Vue, Anthropic, AgentFramework, AgUi, Generative, "frontend.ui-library=reka-ui");

        files[$"{Page}/DashboardAssistant.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The generative dashboards pack builds the Vue assistant from shadcn-vue with vee-validate and zod only so far; frontend.ui-library is reka-ui");
    }

    [Fact]
    public void The_assistant_checks_accessibility_with_axe_only_where_the_project_has_the_accessibility_tests()
    {
        Files(Profile, Anthropic, AgentFramework, AgUi, Generative)[$"{Page}/DashboardAssistant.test.tsx"].ShouldContain("from 'vitest-axe'");

        var without = Files(Profile, Anthropic, AgentFramework, AgUi, Generative, "frontend.test.extras=");

        without["web/app/package.json"].ShouldNotContain("vitest-axe");
        without[$"{Page}/DashboardAssistant.test.tsx"].ShouldNotContain("axe");

        Files(Vue, Anthropic, AgentFramework, AgUi, Generative)[$"{Page}/DashboardAssistant.test.ts"].ShouldContain("from 'vitest-axe'");
        Files(Vue, Anthropic, AgentFramework, AgUi, Generative, "frontend.test.extras=")[$"{Page}/DashboardAssistant.test.ts"].ShouldNotContain("axe");
    }

    [Fact]
    public void The_web_app_builds_lints_and_passes_its_tests_and_the_end_to_end_tests_accept_save_and_reload_a_proposed_widget()
    {
        using var project = NewProject(Profile, Anthropic, AgentFramework, AgUi, Generative);
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        // Sign-in, the assistant's panel, the dashboard page, and the two of the dashboards agent: proposed, accepted, saved
        // and reloaded; and refused, with nothing pending or saved.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("5 passed");
    }

    [Fact]
    public void An_accept_that_saves_at_once_fails_the_assistants_component_tests()
    {
        using var project = NewProject(Profile, Anthropic, AgentFramework, AgUi, Generative);
        var pendingWidget = Path.Combine(project.ProjectDirectory, Page, "PendingWidget.tsx");
        File.Move(pendingWidget, Path.Combine(project.ProjectDirectory, Page, "AcceptingPendingWidget.tsx"));
        project.Write($"{Page}/PendingWidget.tsx", PendingWidgetSavingAtOnce);

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);
        // Without colours, which CI's runners turn on, so the failed test's line is plain text.
        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), new Dictionary<string, string> { ["NO_COLOR"] = "1" }, "pnpm", "test", "src/dashboards/DashboardAssistant.test.tsx");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("FAIL  src/dashboards/DashboardAssistant.test.tsx > a proposed widget shows on the grid as pending with its data, and Accept only puts it on the draft", customMessage: output);
    }

    [Fact]
    public void The_vue_web_app_builds_lints_and_passes_its_tests_and_the_end_to_end_tests_accept_save_and_reload_a_proposed_widget()
    {
        using var project = NewProject(Vue, Anthropic, AgentFramework, AgUi, Generative);
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        // Sign-in, the assistant's panel, the dashboard page, and the two of the dashboards agent, as with React.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("5 passed");
    }

    [Fact]
    public void A_vue_accept_that_saves_at_once_fails_the_assistants_component_tests()
    {
        using var project = NewProject(Vue, Anthropic, AgentFramework, AgUi, Generative);
        var pendingWidget = Path.Combine(project.ProjectDirectory, Page, "PendingWidget.vue");
        File.Move(pendingWidget, Path.Combine(project.ProjectDirectory, Page, "AcceptingPendingWidget.vue"));
        project.Write($"{Page}/PendingWidget.vue", VuePendingWidgetSavingAtOnce);

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);
        // Without colours, which CI's runners turn on, so the failed test's line is plain text.
        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), new Dictionary<string, string> { ["NO_COLOR"] = "1" }, "pnpm", "test", "src/dashboards/DashboardAssistant.test.ts");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("FAIL  src/dashboards/DashboardAssistant.test.ts > a proposed widget shows on the grid as pending with its data, and Accept only puts it on the draft", customMessage: output);
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

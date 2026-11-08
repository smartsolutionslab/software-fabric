using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.agent-ui: ag-ui</c> in a Vue single-page app (ADR-0013, #183): the same panel as React's, on
/// <c>@ag-ui/client</c>'s <c>HttpAgent</c>, built from the project's shadcn-vue components in a vee-validate form with a
/// zod schema, on the <c>assistant</c> module's page. The backend is the one React's panel talks to (lean-crud with an
/// agent); the frontend is saas-standard-vue's.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class AgUiVueTests
{
    private const string Profile = "lean-crud";
    private const string Anthropic = "backend.llm=anthropic";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string AgUi = "frontend.agent-ui=ag-ui";
    private const string Panel = "web/app/src/agents/AgentPanel.vue";
    private const string Conversation = "web/app/src/agents/AgentConversation.vue";
    private const string Composable = "web/app/src/agents/useAgent.ts";
    private const string Client = "web/app/src/agents/connectAgent.ts";
    private const string PanelTest = "web/app/src/agents/AgentPanel.test.ts";
    private const string PanelEndToEndTest = "web/app/e2e/agent-panel.spec.ts";
    private const string AssistantPage = "web/app/src/modules/assistant/AssistantPage.vue";

    /// <summary>saas-standard-vue's frontend choices on lean-crud's backend.</summary>
    private static readonly string[] Vue =
    [
        "frontend.framework=vue",
        "frontend.state=pinia-tanstack-query",
        "frontend.routing=vue-router",
        "frontend.forms=vee-validate",
        "frontend.ui-library=shadcn-vue",
    ];

    [Fact]
    public void A_vue_project_with_an_agent_panel_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[]
        {
            With(Anthropic, AgentFramework, AgUi),
            With(Anthropic, AgentFramework, AgUi, "backend.auth=none"),
            With(Anthropic, AgentFramework, AgUi, "frontend.test.extras="),
        })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }

        Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "saas-standard-vue", [Anthropic, AgentFramework, AgUi]).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_assistants_page_is_the_agent_panel_on_the_ag_ui_client_built_from_the_projects_shadcn_vue_components()
    {
        var files = Files(Profile, With(Anthropic, AgentFramework, AgUi));

        files["web/app/package.json"].ShouldContain("\"@ag-ui/client\": \"1.0.3\"");
        files[AssistantPage].ShouldContain("import AgentPanel from '@/agents/AgentPanel.vue';");
        files[AssistantPage].ShouldContain("<AgentPanel agent=\"assistant\" />");
        files[Panel].ShouldContain("<AgentConversation :key=\"agent\" :agent=\"agent\" />");
        files[Conversation].ShouldContain("import { Button } from '@/components/ui/button';");
        files[Conversation].ShouldContain("import { useForm } from 'vee-validate';");
        files[Conversation].ShouldContain("validationSchema: toTypedSchema(messageSchema),");
        files[Composable].ShouldContain("from 'vue';");
        files[Composable].ShouldNotContain("react");
        files[Client].ShouldContain("new HttpAgent(");
        files[Client].ShouldContain("url: `/api/agents/${name}`");
        files[PanelTest].ShouldContain("from '@testing-library/vue';");
        files[PanelTest].ShouldNotContain("throw new Error(");
        files.Keys.ShouldContain(PanelEndToEndTest);
        files.Keys.ShouldNotContain("web/app/src/agents/AgentPanel.tsx");
        files.Keys.ShouldNotContain("web/app/src/agents/AgentPanel.test.tsx");
        files["AGENTS.md"].ShouldContain("AgentPanel.vue");
    }

    [Fact]
    public void The_vue_panels_client_sends_the_signed_in_users_token_as_the_api_client_does_and_none_without_authentication()
    {
        var files = Files(Profile, With(Anthropic, AgentFramework, AgUi));

        files[Client].ShouldContain("headers.set('Authorization', `Bearer ${token}`);");
        files[PanelTest].ShouldContain("toBe('Bearer token-of-developer')");

        var open = Files(Profile, With(Anthropic, AgentFramework, AgUi, "backend.auth=none"));

        open[Client].ShouldNotContain("Authorization");
        open[PanelTest].ShouldNotContain("oidcConfig");
        open[PanelEndToEndTest].ShouldNotContain("Sign In");
    }

    [Fact]
    public void The_vue_panel_checks_accessibility_with_axe_only_where_the_project_has_the_accessibility_tests()
    {
        Files(Profile, With(Anthropic, AgentFramework, AgUi))[PanelTest].ShouldContain("from 'vitest-axe'");

        var without = Files(Profile, With(Anthropic, AgentFramework, AgUi, "frontend.test.extras="));

        without["web/app/package.json"].ShouldNotContain("vitest-axe");
        without[PanelTest].ShouldNotContain("axe");
    }

    [Fact]
    public void Another_vue_ui_library_fails_the_panel_saying_it_is_not_built_for_it_yet()
    {
        var files = Files(Profile, [.. Vue.Where(choice => !choice.StartsWith("frontend.ui-library=", StringComparison.Ordinal)), "frontend.ui-library=reka-ui", Anthropic, AgentFramework, AgUi]);

        files[Conversation].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The ag-ui pack builds the Vue agent panel from shadcn-vue with vee-validate and zod or valibot only so far; frontend.ui-library is reka-ui");
    }

    [Fact]
    public void The_vue_web_app_builds_lints_and_passes_its_tests_and_the_end_to_end_test_talks_to_the_assistant_twice_through_the_panel()
    {
        using var project = NewProject(With(Anthropic, AgentFramework, AgUi));
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        // The generated CI's own end-to-end step: the sign-in test and the panel's, against the AppHost with the stand-in model.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("2 passed");
    }

    private static string[] With(params string[] overrides) => [.. Vue, .. overrides];

    private static Dictionary<string, string> Files(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A lean-crud project with the overrides; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string[] overrides)
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

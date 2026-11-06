using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.agent-ui</c> (ADR-0013): whether a project with agents gives its users an agent panel over AG-UI. With
/// <c>none</c>, the web app imports no AG-UI or CopilotKit client and no module references the AG-UI .NET SDK; the
/// choice exists only for a project with agents, so no profile changes.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class AgentUiTests
{
    private const string Profile = "lean-crud";
    private const string Anthropic = "backend.llm=anthropic";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string Rule = "tests/Architecture.Tests/Rules/NoAgentUi.cs";

    private const string ApiReferencingTheSdk = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="AGUI.Server" VersionOverride="1.0.1" />
          </ItemGroup>
        </Project>
        """;

    private const string EndpointStreamingAgUi = """
        using AGUI.Abstractions;

        namespace Shop.Modules.Billing.Api;

        public static class AgentRuns
        {
            public static string Thread(RunAgentInput input) => input.ThreadId;
        }
        """;

    [Fact]
    public void A_project_with_agents_and_no_agent_panel_generates_from_the_real_packs_alone()
    {
        foreach (var profile in new[] { Profile, "saas-standard-react", "saas-standard-vue" })
        {
            var files = Files(profile, Anthropic, AgentFramework);

            files["fabric.yaml"].ShouldContain("frontend.agent-ui: none", customMessage: profile);
        }
    }

    [Fact]
    public void Without_an_agent_panel_the_web_app_refuses_the_ag_ui_and_copilotkit_clients_and_modules_get_the_rule()
    {
        var files = Files(Profile, Anthropic, AgentFramework);

        files["web/app/eslint.config.js"].ShouldContain("group: ['@ag-ui/*', '@copilotkit/*']");
        files[Rule].ShouldContain("class NoAgentUi");
        files["AGENTS.md"].ShouldContain("## Agent UI: none");
    }

    [Fact]
    public void A_profile_without_agents_does_not_change_and_one_with_the_agent_panel_has_no_none_rule()
    {
        var withoutAgents = Repository.ProfilesNotChoosing("backend.llm.agents", "agent-framework");
        var withThePanel = Repository.ProfilesChoosing("frontend.agent-ui", "ag-ui");
        withoutAgents.ShouldNotBeEmpty();
        withThePanel.ShouldNotBeEmpty();

        foreach (var profile in withThePanel)
        {
            var files = Files(profile);

            files["fabric.yaml"].ShouldContain("  frontend.agent-ui: ag-ui\n", customMessage: profile);
            files.Keys.ShouldNotContain(Rule, profile);
        }

        foreach (var profile in withoutAgents)
        {
            var files = Files(profile);

            files["fabric.yaml"].ShouldNotContain("frontend.agent-ui", customMessage: profile);
            files.Keys.ShouldNotContain(Rule, profile);
            files["web/app/eslint.config.js"].ShouldNotContain("@ag-ui", customMessage: profile);
        }
    }

    [Fact]
    public void Without_an_agent_panel_importing_the_ag_ui_client_fails_lint()
    {
        using var project = NewProject(Anthropic, AgentFramework);
        project.Write("web/app/src/agent.ts", """
            import { HttpAgent } from '@ag-ui/client';

            export const agent = new HttpAgent({ url: '/api/agents/assistant' });
            """);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);

        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'@ag-ui/client' import is restricted from being used by a pattern. This project has no agent panel (frontend.agent-ui: none)");
    }

    [Fact]
    public void Without_an_agent_panel_a_module_referencing_the_ag_ui_sdk_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Anthropic, AgentFramework);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/Billing.Api.csproj", ApiReferencingTheSdk);
        project.WriteTypes("src/Modules/Billing/Billing.Api", EndpointStreamingAgUi);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Api references AGUI.Abstractions, but this project has no agent panel (frontend.agent-ui: none)");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

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

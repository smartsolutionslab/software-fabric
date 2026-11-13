using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.agent-ui: ag-ui</c>, its backend (ADR-0013, #202): one host endpoint, <c>POST /agents/{name}</c>, added
/// through the host slots, which reads the AG-UI input, sends the named agent's <c>IAgentConversation</c> query through
/// the dispatcher and streams the agent's updates back as AG-UI events. The pack brings an <c>assistant</c> module whose
/// agent counts words through the dispatcher, and a Development-only stand-in model the AppHost parameter
/// <c>model-stand-in</c> switches on.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class AgUiTests
{
    private const string Profile = "lean-crud";
    private const string Anthropic = "backend.llm=anthropic";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string AgUi = "frontend.agent-ui=ag-ui";
    private const string Rule = "tests/Architecture.Tests/Rules/AgentUiGoesThroughAQuery.cs";
    private const string Endpoints = "src/Host/Wiring/AgentUiEndpoints.cs";
    private const string Assistant = "src/Modules/Assistant/Assistant.Application";
    private const string Panel = "web/app/src/agents/AgentPanel.tsx";
    private const string Client = "web/app/src/agents/connectAgent.ts";
    private const string PanelEndToEndTest = "web/app/e2e/agent-panel.spec.ts";
    private const string EndToEndScript = ".github/scripts/e2e.sh";

    private const string ApiStreamingAnAgent = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="AGUI.Server" />
            <PackageReference Include="AGUI.Formatting" />
          </ItemGroup>
        </Project>
        """;

    private const string EndpointStreamingAnAgentDirectly = """
        using System.Text.Json;
        using AGUI.Abstractions;
        using AGUI.Formatting;
        using AGUI.Server;
        using Microsoft.Agents.AI;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.AI;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api.Endpoints;

        public sealed class StreamAnAgent : IEndpoint
        {
            private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapPost("/agent", async (RunAgentInput input, IChatClient model, HttpResponse response, CancellationToken cancellationToken) =>
                {
                    var context = input.ToChatRequestContext(Json);
                    var updates = model.AsAIAgent(instructions: "Answer.").RunStreamingAsync(context.Messages, cancellationToken: cancellationToken).AsChatResponseUpdatesAsync();
                    await new SseEventStreamFormatter().WriteAsync(updates.AsAGUIEventStreamAsync(context, cancellationToken), response.Body, cancellationToken);
                });
        }
        """;

    private const string HostRunningAnAgent = """
        using Microsoft.Agents.AI;
        using Microsoft.Extensions.AI;

        namespace Shop.Host.Agents;

        public static class DirectAgent
        {
            public static AIAgent Create(IChatClient model) => model.AsAIAgent(instructions: "Answer.");
        }
        """;

    /// <summary>
    /// A screen calling the host by hand: <c>fetch</c> with a bearer token it sets itself, <c>globalThis.fetch</c> with a
    /// header set by name, a second <c>HttpAgent</c> and an <c>XMLHttpRequest</c>.
    /// </summary>
    private const string SecondCallToTheHost = """
        import { HttpAgent } from '@ag-ui/client';

        export async function loadNotes(token: string) {
          const response = await fetch('/api/billing/notes', { headers: { Authorization: `Bearer ${token}` } });
          return (await response.json()) as unknown;
        }

        export async function loadNote(id: string) {
          const headers = new Headers();
          headers.set('Authorization', 'Bearer not-a-token');
          return globalThis.fetch(`/api/billing/notes/${id}`, { headers });
        }

        export const secondAgent = new HttpAgent({ url: '/api/agents/assistant' });

        export function request() {
          return new XMLHttpRequest();
        }

        """;

    /// <summary>
    /// The header written through a member, by name and computed, then words that say authorization without setting a
    /// header: a constant and a field of unrelated data.
    /// </summary>
    private const string HeaderWrittenThroughAMember = """
        export const section = 'authorization';

        export function setToken(headers: { Authorization?: string }, token: string) {
          const assigned = headers;
          assigned.Authorization = token;
        }

        export function setLowerCase(headers: Record<string, string>, token: string) {
          headers['authorization'] = token;
        }

        export const permissions = { scope: 'authorization', role: 'manager' };

        """;

    /// <summary>A component whose template builds a second <c>HttpAgent</c> and sets the header by hand.</summary>
    private const string CallToTheHostInATemplate = """
        <script setup lang="ts">
        import { HttpAgent } from '@ag-ui/client';

        const headers = new Headers();
        defineExpose({ HttpAgent, headers });
        </script>

        <template>
          <button type="button" @click="new HttpAgent({ url: '/api/agents/assistant' })">Ask</button>
          <button type="button" @click="headers.set('Authorization', 'Bearer not-a-token')">Sign</button>
        </template>

        """;

    /// <summary>
    /// Data that says authorization without being a header, a map entry and a field, then the header set in an object
    /// held as headers and in one given to <c>new Headers</c>.
    /// </summary>
    private const string AuthorizationOutsideAndInsideHeaders = """
        export const sections = new Map<string, string>().set('section', 'authorization');

        export const grant = { authorization: 'manager' };

        export function sign(token: string) {
          const requestHeaders: Record<string, string> = { Authorization: `Bearer ${token}` };
          return new Headers({ authorization: token, ...requestHeaders });
        }

        """;

    /// <summary>
    /// A component whose template calls the host through <c>window.fetch</c>, <c>fetch</c> and an
    /// <c>XMLHttpRequest</c>, and fills a map entry that merely says authorization.
    /// </summary>
    private const string FetchInATemplate = """
        <script setup lang="ts">
        const window = globalThis.window;
        const grants = new Map<string, string>();
        defineExpose({ window, grants });
        </script>

        <template>
          <button type="button" @click="window.fetch('/api/agents/assistant', { method: 'POST' })">Ask</button>
          <button type="button" @click="fetch('/api/billing/notes')">Load</button>
          <button type="button" @click="new XMLHttpRequest()">Open</button>
          <button type="button" @click="grants.set('section', 'authorization')">Grant</button>
        </template>

        """;

    /// <summary>
    /// A map keyed authorization and a field of that name assigned, neither a header, then the header set on a
    /// <c>Headers</c> and on a fetch init's headers.
    /// </summary>
    private const string AuthorizationKeysOutsideAndInsideHeaders = """
        export const roles = new Map<string, string>().set('authorization', 'x');

        export function revoke(grant: { authorization?: string }) {
          grant.authorization = 'x';
        }

        export function sign(headers: Headers, init: { headers: Headers }, token: string) {
          headers.append('authorization', token);
          init.headers.set('Authorization', token);
        }

        """;

    /// <summary>
    /// A component whose template keys a map and a field authorization, neither a header, and sets the header on the
    /// <c>Headers</c> its script exposes.
    /// </summary>
    private const string AuthorizationKeysInATemplate = """
        <script setup lang="ts">
        const roles = new Map<string, string>();
        const grant: { authorization?: string } = {};
        const requestHeaders = new Headers();
        defineExpose({ roles, grant, requestHeaders });
        </script>

        <template>
          <button type="button" @click="roles.set('authorization', 'x')">Role</button>
          <button type="button" @click="grant.authorization = 'x'">Grant</button>
          <button type="button" @click="requestHeaders.set('authorization', 'x')">Sign</button>
        </template>

        """;

    /// <summary>The rules eslint names for <see cref="SecondCallToTheHost"/>, in the order of its lines.</summary>
    private const string SecondCallToTheHostFindings =
        "no-restricted-globals, no-restricted-syntax, no-restricted-syntax, no-restricted-properties, no-restricted-syntax, no-restricted-globals";

    /// <summary>
    /// Runs the AG-UI SDK's own stream over updates that fail mid-stream, with the tracer <c>AddServiceDefaults()</c>
    /// builds. The browser's RUN_ERROR says only that the run failed; the <c>agui.run</c> span is where the cause is.
    /// The architecture tests reference the host, and through it the SDK, because keycloak's endpoint rule needs it.
    /// </summary>
    private const string AgentRunTraceTests = """
        using System.Diagnostics;
        using System.Text.Json;
        using System.Text.Json.Serialization.Metadata;
        using AGUI.Abstractions;
        using AGUI.Server;
        using Microsoft.Extensions.AI;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using OpenTelemetry;
        using OpenTelemetry.Trace;

        namespace Shop.Architecture.Tests.Probes;

        public sealed class AgentRunTraceTests
        {
            private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(AGUIJsonUtilities.DefaultTypeInfoResolver, AIJsonUtilities.DefaultOptions.TypeInfoResolver),
            };

            [Fact]
            public async Task A_run_that_fails_mid_stream_tells_the_browser_only_that_it_failed_and_the_trace_why()
            {
                var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(settings: null);
                builder.AddServiceDefaults();
                List<Activity> ended = [];
                builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new EndedRuns(ended)));
                using var provider = builder.Services.BuildServiceProvider();
                provider.GetRequiredService<TracerProvider>();

                var context = new RunAgentInput { ThreadId = "thread-1", RunId = "run-1" }.ToChatRequestContext(Json);
                List<BaseEvent> events = [];
                await foreach (var agUiEvent in FailingMidStream().AsAGUIEventStreamAsync(context, TestContext.Current.CancellationToken))
                {
                    events.Add(agUiEvent);
                }

                events.OfType<RunErrorEvent>().ShouldHaveSingleItem().Message.ShouldNotContain("The model stopped answering.");
                var run = ended.ShouldHaveSingleItem();
                run.GetTagItem("agui.run_id").ShouldBe("run-1");
                run.GetTagItem("agui.run.outcome").ShouldBe("error");
                run.Status.ShouldBe(ActivityStatusCode.Error);
                run.StatusDescription.ShouldBe("The model stopped answering.");
            }

            private static async IAsyncEnumerable<ChatResponseUpdate> FailingMidStream()
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Counting the");
                await Task.Yield();
                throw new InvalidOperationException("The model stopped answering.");
            }
        }

        internal sealed class EndedRuns(List<Activity> runs) : BaseProcessor<Activity>
        {
            public override void OnEnd(Activity data)
            {
                if (data.OperationName == "agui.run")
                {
                    runs.Add(data);
                }
            }
        }
        """;

    [Fact]
    public void A_project_with_an_agent_panel_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[]
        {
            new[] { Anthropic, AgentFramework, AgUi },
            [Anthropic, AgentFramework, AgUi, "backend.auth=none"],
            [Anthropic, AgentFramework, AgUi, "backend.absence=nullable", "backend.result-style=minimal", "backend.guards=bcl"],
        })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }

        Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "saas-standard-react", [Anthropic, AgentFramework, AgUi]).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_host_maps_one_agent_endpoint_through_its_slots_and_requires_sign_in()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddAgentUi(builder.Configuration, builder.Environment);");
        files["src/Host/Program.cs"].ShouldContain("app.MapAgentUi();");
        files[Endpoints].ShouldContain("app.MapPost(\"/agents/{name}\", AgentRuns.RunAsync)");
        files[Endpoints].ShouldContain(".RequireAuthorization()");
        files[Endpoints].ShouldContain(".ExcludeFromDescription()");
        files["src/Host/Agents/AgentRuns.cs"].ShouldContain("dispatcher.QueryAsync(");
        files["src/BuildingBlocks/Agents/IAgentConversation.cs"].ShouldContain("IQuery<Result<IAsyncEnumerable<ChatResponseUpdate>, ApiError>>");
    }

    [Fact]
    public void Only_the_host_references_the_ag_ui_sdk_and_its_gate_replaces_the_rule_against_a_panel()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        foreach (var package in new[] { "AGUI.Server", "AGUI.Formatting" })
        {
            Count(files["Directory.Packages.props"], $"<PackageVersion Include=\"{package}\" Version=\"1.0.1\" />").ShouldBe(1, package);
            files["src/Host/Host.csproj"].ShouldContain($"<PackageReference Include=\"{package}\" />");
        }

        files["src/Modules/Directory.Build.props"].ShouldNotContain("AGUI");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldNotContain("AGUI");
        files["Directory.Packages.props"].ShouldNotContain("Microsoft.Agents.AI.Hosting");
        files[Rule].ShouldContain("class AgentUiGoesThroughAQuery");
        files.Keys.ShouldNotContain("tests/Architecture.Tests/Rules/NoAgentUi.cs");
        files["AGENTS.md"].ShouldContain("## Agent UI: AG-UI");
    }

    [Fact]
    public void Without_authentication_the_agent_endpoint_is_open_to_anyone()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi, "backend.auth=none");

        files[Endpoints].ShouldContain("app.MapPost(\"/agents/{name}\", AgentRuns.RunAsync)");
        files[Endpoints].ShouldNotContain("RequireAuthorization");
    }

    [Fact]
    public void The_pack_brings_an_assistant_module_whose_agent_counts_words_through_the_dispatcher()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        files["fabric.yaml"].ShouldContain("""
              assistant:
                module.style: crud
                module.cqrs: logical
            """);
        files[$"{Assistant}/Agents/AssistantAgent.cs"].ShouldContain("DispatcherTool.For<CountWords>(dispatcher,");
        files[$"{Assistant}/Queries/TalkToTheAssistant/TalkToTheAssistant.cs"].ShouldContain("IAgentConversation<TalkToTheAssistant>");
        files[$"{Assistant}/Queries/TalkToTheAssistant/TalkToTheAssistant.cs"].ShouldContain("public static string AgentName => \"assistant\";");
        files[$"{Assistant}/Queries/TalkToTheAssistant/TalkToTheAssistantHandler.cs"].ShouldContain("RunStreamingAsync(");
        files[$"{Assistant}/Queries/CountWords/CountWordsHandler.cs"].ShouldContain("IQueryHandler<CountWords, Result<int, ApiError>>");
        files["tests/Integration.Tests/AgentUiTests.cs"].ShouldContain("/agents/assistant");
    }

    [Fact]
    public void The_stand_in_model_is_switched_on_only_by_the_apphost_parameter_the_aspire_fixture_sets()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        files["src/AppHost/AppHost.cs"].ShouldContain("builder.AddParameter(\"model-stand-in\",");
        files["src/AppHost/AppHost.cs"].ShouldContain(".WithEnvironment(\"Agents__ModelStandIn\", modelStandIn)");
        files["tests/Integration.Tests/AspireFixture.cs"].ShouldContain("builder.Configuration[\"Parameters:model-stand-in\"] = \"true\";");
        files["src/Host/Wiring/AgentUiRegistration.cs"].ShouldContain("environment.IsDevelopment()");
        files["src/Host/Agents/StandInChatClient.cs"].ShouldContain("class StandInChatClient : IChatClient");
    }

    [Fact]
    public void The_service_defaults_listen_to_the_ag_ui_run_spans()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        files["src/ServiceDefaults/Wiring/Extensions.cs"].ShouldContain(".AddSource(\"Experimental.AGUI.Server\")");
    }

    [Fact]
    public void A_profile_without_the_agent_panel_does_not_change_and_one_with_it_gets_the_panel()
    {
        var without = Repository.ProfilesNotChoosing("frontend.agent-ui", "ag-ui");
        var with = Repository.ProfilesChoosing("frontend.agent-ui", "ag-ui");
        without.ShouldNotBeEmpty();
        with.ShouldNotBeEmpty();

        foreach (var profile in with)
        {
            var files = Files(profile);

            files.Keys.ShouldContain(path => path.StartsWith("web/app/src/agents/", StringComparison.Ordinal), profile);
            files[EndToEndScript].ShouldContain("--Parameters:model-stand-in=true", customMessage: profile);
            files["src/ServiceDefaults/Wiring/Extensions.cs"].ShouldContain("AGUI", customMessage: profile);
            files["fabric.yaml"].ShouldContain("  assistant:", customMessage: profile);
        }

        foreach (var profile in without)
        {
            var files = Files(profile);

            files.Keys.ShouldNotContain(path => path.Contains("AgentUi", StringComparison.Ordinal) || path.Contains("Assistant", StringComparison.Ordinal), profile);
            files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/agents/", StringComparison.Ordinal) || path.Contains("agent-panel", StringComparison.Ordinal), profile);
            files["src/AppHost/AppHost.cs"].ShouldNotContain("model-stand-in", customMessage: profile);
            files[EndToEndScript].ShouldNotContain("model-stand-in", customMessage: profile);
            files["src/ServiceDefaults/Wiring/Extensions.cs"].ShouldNotContain("AGUI", customMessage: profile);
            files["fabric.yaml"].ShouldNotContain("assistant", customMessage: profile);
        }
    }

    [Fact]
    public void The_assistants_page_is_the_agent_panel_on_the_ag_ui_client_built_from_the_projects_shadcn_components()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        files["web/app/package.json"].ShouldContain("\"@ag-ui/client\": \"1.0.4\"");
        files["web/app/src/modules/assistant/AssistantPage.tsx"].ShouldContain("<AgentPanel agent=\"assistant\" />");
        files[Panel].ShouldContain("from '@/components/ui/button'");
        files[Panel].ShouldContain("zodResolver(");
        files[Client].ShouldContain("new HttpAgent(");
        files[Client].ShouldContain("url: `/api/agents/${name}`");
        files.Keys.ShouldContain("web/app/src/agents/AgentPanel.test.tsx");
        files.Keys.ShouldContain(PanelEndToEndTest);
        files["AGENTS.md"].ShouldContain("AgentPanel");
    }

    [Fact]
    public void The_panels_client_sends_the_signed_in_users_token_as_the_api_client_does_and_none_without_authentication()
    {
        Files(Profile, Anthropic, AgentFramework, AgUi)[Client].ShouldContain("headers.set('Authorization', `Bearer ${token}`);");

        var open = Files(Profile, Anthropic, AgentFramework, AgUi, "backend.auth=none");

        open[Client].ShouldNotContain("Authorization");
        open[Client].ShouldNotContain("accessToken");
        open[PanelEndToEndTest].ShouldNotContain("Sign In");
    }

    [Fact]
    public void Importing_copilotkit_fails_the_web_apps_lint_with_the_agent_panel()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        // Prettier wraps the entry (printWidth 120), so its group and message are on lines of their own.
        files["web/app/eslint.config.js"].ShouldContain("group: ['@copilotkit/*'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Talk to the agents through the AgentPanel in src/agents, on @ag-ui/client (docs/adr).',");
        files["web/app/eslint.config.js"].ShouldNotContain("'@ag-ui/*'");
    }

    /// <summary>
    /// The owner's decision on #607: AG-UI's run is a server-sent event stream the generated client cannot make, so
    /// <c>connectAgent</c> alone calls the host by hand, with the token from where <c>src/api/baseApi.ts</c> reads it.
    /// </summary>
    [Fact]
    public void The_agent_panels_client_is_the_one_hand_written_call_to_the_host_and_reads_the_token_where_the_api_client_does()
    {
        foreach (var (description, files) in new[]
        {
            ("oidc-client-ts", Files(Profile, Anthropic, AgentFramework, AgUi)),
            ("msal", Files(Profile, Anthropic, AgentFramework, AgUi, "backend.auth=entra-id", "frontend.auth-client=msal")),
        })
        {
            const string TokenImport = "import { accessToken } from '@/auth/accessToken';";
            files["web/app/src/api/baseApi.ts"].ShouldContain(TokenImport, customMessage: description);
            files[Client].ShouldContain(TokenImport, customMessage: description);
            files[Client].ShouldContain("const token = accessToken();", customMessage: description);
        }

        var react = Files(Profile, Anthropic, AgentFramework, AgUi);
        react["web/app/eslint.config.js"].ShouldContain("files: ['src/**/*.{ts,tsx}'],");
        react["web/app/eslint.config.js"].ShouldContain("ignores: ['src/api/baseApi.ts', 'src/agents/connectAgent.ts', '**/*.test.*'],");
        react["AGENTS.md"].ShouldContain("`src/agents/connectAgent.ts` is the one exception to the API client's rule");
        AgentUiAdr(react).ShouldContain("`connectAgent` is the one call to the host the generated client does not make");

        Files("saas-standard-vue", Anthropic, AgentFramework, AgUi)["web/app/eslint.config.js"].ShouldContain("files: ['src/**/*.{ts,vue}'],");

        // Without the panel, nothing is excepted, so nothing is added.
        Files(Profile, Anthropic, AgentFramework)["web/app/eslint.config.js"].ShouldNotContain("no-restricted-globals");
    }

    [Fact]
    public void A_second_hand_written_call_to_the_host_or_a_hand_set_authorization_header_fails_the_web_apps_lint()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);
        var (untouchedExitCode, untouchedOutput) = Shell.Run(web, "pnpm", "lint");
        untouchedExitCode.ShouldBe(0, untouchedOutput);

        project.Write("web/app/src/pages/notes.ts", SecondCallToTheHost);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe(SecondCallToTheHostFindings, output);
        output.ShouldContain("only src/agents/connectAgent.ts calls the host by hand");
    }

    [Fact]
    public void With_vue_a_second_hand_written_call_to_the_host_or_a_hand_set_authorization_header_fails_the_web_apps_lint()
    {
        using var project = new GeneratedProject("saas-standard-vue", Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);
        var (untouchedExitCode, untouchedOutput) = Shell.Run(web, "pnpm", "lint");
        untouchedExitCode.ShouldBe(0, untouchedOutput);

        project.Write("web/app/src/pages/notes.ts", SecondCallToTheHost);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe(SecondCallToTheHostFindings, output);
        output.ShouldContain("only src/agents/connectAgent.ts calls the host by hand");
    }

    /// <summary>
    /// Review of #638: a header written through a member fails lint as one written by a literal does, and a word that
    /// merely says authorization does not.
    /// </summary>
    [Fact]
    public void An_authorization_header_written_through_a_member_fails_the_web_apps_lint_and_other_authorization_words_do_not()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);

        project.Write("web/app/src/pages/token.ts", HeaderWrittenThroughAMember);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe("no-restricted-syntax, no-restricted-syntax", output);
        output.ShouldContain("5:3 ");
        output.ShouldContain("9:3 ");
    }

    /// <summary>Review of #638: eslint's own rules do not read a Vue template, so the gate holds there too.</summary>
    [Fact]
    public void With_vue_a_hand_written_call_to_the_host_in_a_components_template_fails_the_web_apps_lint()
    {
        using var project = new GeneratedProject("saas-standard-vue", Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);

        project.Write("web/app/src/pages/NotesPage.vue", CallToTheHostInATemplate);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe("vue/no-restricted-syntax, vue/no-restricted-syntax", output);
    }

    /// <summary>
    /// Second review of #638: only a header's name is refused, so a map entry or a field that says authorization
    /// passes, while an object of headers that sets it still fails.
    /// </summary>
    [Fact]
    public void Data_that_merely_says_authorization_passes_the_web_apps_lint_and_an_object_of_headers_setting_it_does_not()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);

        project.Write("web/app/src/pages/grants.ts", AuthorizationOutsideAndInsideHeaders);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe("no-restricted-syntax, no-restricted-syntax", output);
        output.ShouldContain("6:52 ");
        output.ShouldContain("7:24 ");
    }

    /// <summary>Second review of #638: a Vue template cannot call the host by hand either.</summary>
    [Fact]
    public void With_vue_a_fetch_in_a_components_template_fails_the_web_apps_lint_and_a_map_entry_saying_authorization_does_not()
    {
        using var project = new GeneratedProject("saas-standard-vue", Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);

        project.Write("web/app/src/pages/AskPage.vue", FetchInATemplate);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe("vue/no-restricted-syntax, vue/no-restricted-syntax, vue/no-restricted-syntax", output);
        output.ShouldContain("only src/agents/connectAgent.ts calls the host by hand");
    }

    /// <summary>
    /// Third review of #638: a map keyed authorization or a field of that name passes; the header set on headers fails.
    /// </summary>
    [Fact]
    public void A_map_or_a_field_keyed_authorization_passes_the_web_apps_lint_and_the_header_set_on_headers_does_not()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);

        project.Write("web/app/src/pages/roles.ts", AuthorizationKeysOutsideAndInsideHeaders);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe("no-restricted-syntax, no-restricted-syntax", output);
        output.ShouldContain("8:18 ");
        output.ShouldContain("9:20 ");
    }

    /// <summary>Third review of #638: in a Vue template too, only the header set on headers fails.</summary>
    [Fact]
    public void With_vue_a_map_or_a_field_keyed_authorization_in_a_template_passes_the_web_apps_lint_and_the_header_does_not()
    {
        using var project = new GeneratedProject("saas-standard-vue", Anthropic, AgentFramework, AgUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, installOutput);

        project.Write("web/app/src/pages/RolesPage.vue", AuthorizationKeysInATemplate);
        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        LintFindings(output).ShouldBe("vue/no-restricted-syntax", output);
        output.ShouldContain("11:");
    }

    [Fact]
    public void The_end_to_end_script_starts_the_apphost_with_the_stand_in_model()
    {
        var files = Files(Profile, Anthropic, AgentFramework, AgUi);

        // The provider's key too, as the AspireFixture gives it: without a value the AppHost waits for its secret parameter
        // and never starts the host. Arguments, not environment variables: Aspire's CLI, which `dotnet run` hands the
        // AppHost to, does not pass on a variable whose name has a hyphen.
        files[EndToEndScript].ShouldContain("dotnet run --project src/AppHost --launch-profile http -- --Parameters:anthropic-api-key=not-a-real-key --Parameters:model-stand-in=true < /dev/null > \"$log\" 2>&1 &");
        Files("saas-standard-vue", Anthropic, AgentFramework, AgUi)[EndToEndScript].ShouldContain("--Parameters:model-stand-in=true < /dev/null");
    }

    [Fact]
    public void The_end_to_end_script_gives_the_apphost_the_key_shaped_values_the_aspire_fixture_gives_the_model_provider()
    {
        foreach (var (provider, values) in new[]
        {
            ("backend.llm=openai", "-- --Parameters:openai-api-key=not-a-real-key --Parameters:model-stand-in=true < /dev/null"),
            ("backend.llm=azure-openai", "-- --Parameters:azure-openai-endpoint=https://not-a-real-resource.openai.azure.com --Parameters:azure-openai-api-key=not-a-real-key --Parameters:model-stand-in=true < /dev/null"),
        })
        {
            Files(Profile, provider, AgentFramework, AgUi)[EndToEndScript].ShouldContain(values, customMessage: provider);
        }

        // Without the agent panel the script gives the AppHost the provider's key alone.
        Files(Profile, Anthropic)[EndToEndScript].ShouldContain("--launch-profile http -- --Parameters:anthropic-api-key=not-a-real-key < /dev/null");
    }

    [Fact]
    public void The_panel_checks_accessibility_with_axe_only_where_the_project_has_the_accessibility_tests()
    {
        Files(Profile, Anthropic, AgentFramework, AgUi)["web/app/src/agents/AgentPanel.test.tsx"].ShouldContain("from 'vitest-axe'");

        var without = Files(Profile, Anthropic, AgentFramework, AgUi, "frontend.test.extras=");

        without["web/app/package.json"].ShouldNotContain("vitest-axe");
        without["web/app/src/agents/AgentPanel.test.tsx"].ShouldNotContain("axe");
    }

    /// <summary>The panel is built for a single-page app only, so the catalog refuses it elsewhere (#518).</summary>
    [Fact]
    public void The_panel_outside_a_single_page_app_is_refused_by_the_catalog()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "industrial-event-driven-mfe", [Anthropic, AgentFramework, AgUi]);

        generated.Errors.ShouldBe(["frontend.agent-ui=ag-ui requires frontend.architecture == spa, but frontend.architecture is micro-frontends."]);
        Files(Profile, Anthropic, AgentFramework, AgUi).Keys.ShouldNotContain("web/app/src/agents/AgentPanel.test.ts");
    }

    [Fact]
    public void The_web_app_builds_lints_and_passes_its_tests_and_the_end_to_end_test_talks_to_the_assistant_twice_through_the_panel()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
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

    [Fact]
    public void The_untouched_project_builds_and_its_architecture_and_assistant_tests_pass()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        var (assistantExitCode, assistantOutput) = project.Test("tests/Modules/Assistant.Tests/Assistant.Tests.csproj");
        assistantExitCode.ShouldBe(0, assistantOutput);
        assistantOutput.ShouldContain("failed: 0");
        assistantOutput.ShouldContain("succeeded: 4");
    }

    [Fact]
    public void With_minimal_results_bcl_guards_and_nullable_absence_the_project_builds_and_its_assistant_tests_pass()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi, "backend.absence=nullable", "backend.result-style=minimal", "backend.guards=bcl");
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Assistant.Tests/Assistant.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 4");
    }

    [Fact]
    public void A_module_endpoint_streaming_an_agent_directly_and_a_host_running_one_fail_the_architecture_tests_naming_them()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/Billing.Api.csproj", ApiStreamingAnAgent);
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", EndpointStreamingAnAgentDirectly);
        var hostProject = Path.Combine(project.ProjectDirectory, "src", "Host", "Host.csproj");
        project.Write("src/Host/Host.csproj", File.ReadAllText(hostProject).Replace(
            "</Project>",
            "  <ItemGroup>\n    <PackageReference Include=\"Microsoft.Agents.AI\" />\n  </ItemGroup>\n\n</Project>",
            StringComparison.Ordinal));
        project.WriteTypes("src/Host/Agents", HostRunningAnAgent);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("failed Shop.Architecture.Tests.ArchitectureRulesTests.Rule_holds(rule: \"AgentUiGoesThroughAQuery\")", customMessage: output);
        output.ShouldContain("Shop.Modules.Billing.Api references AGUI.Server, but only the host speaks AG-UI", customMessage: output);
        output.ShouldContain("Host references Microsoft.Agents.AI, but the host runs no agent", customMessage: output);
    }

    [Fact]
    public void A_run_that_fails_mid_stream_is_traced_with_its_cause_through_the_service_defaults()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        project.WriteTypes("tests/Architecture.Tests/Probes", AgentRunTraceTests);
        project.Build();

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Architecture.Tests/Architecture.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-class", "Shop.Architecture.Tests.Probes.AgentRunTraceTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("succeeded: 1", customMessage: output);
        output.ShouldContain("failed: 0", customMessage: output);
    }

    [Fact]
    public void The_integration_tests_stream_the_assistants_answer_in_several_chunks_and_run_its_tool_through_the_dispatcher()
    {
        using var project = NewProject(Anthropic, AgentFramework, AgUi);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        // Before the exit code: the copy failing is a failed test too, and this names the cause.
        output.ShouldNotContain("The_frontends_copy_of_the_openapi_document", customMessage: output);
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("skipped: 1");
    }

    [Fact]
    public void A_second_answer_sent_in_one_chunk_fails_the_integration_test_of_the_second_turn()
    {
        using var project = WithTheSecondAnswerInOneChunk();
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain(
            "failed Shop.Integration.Tests.AgentUiTests.In_a_second_turn_the_tool_runs_again_for_the_new_message_and_the_answer_comes_from_that_result",
            customMessage: output);
        output.ShouldContain("failed: 1", customMessage: output);
    }

    [Fact]
    public void A_second_answer_sent_in_one_chunk_fails_the_panels_end_to_end_test()
    {
        using var project = WithTheSecondAnswerInOneChunk();
        project.Build();
        var (installExitCode, install) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "install");
        installExitCode.ShouldBe(0, install);

        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("agent-panel.spec.ts", customMessage: output);
        output.ShouldContain("1 failed", customMessage: output);
        output.ShouldContain("1 passed", customMessage: output);
    }

    /// <summary>
    /// A project whose stand-in model answers "four five", the second turn of the integration and end-to-end tests, in one
    /// TEXT_MESSAGE_CONTENT event, and every other message a word at a time.
    /// </summary>
    private static GeneratedProject WithTheSecondAnswerInOneChunk()
    {
        var project = NewProject(Anthropic, AgentFramework, AgUi);
        var path = Path.Combine(project.ProjectDirectory, "src", "Host", "Agents", "StandInChatClient.cs");
        var streamed = File.ReadAllText(path);
        var inOneChunk = streamed.Replace(
            "foreach (var chunk in Chunks(answer))",
            "var chunks = string.Equals(question, \"four five\", StringComparison.Ordinal) ? Enumerable.Repeat(answer, 1) : Chunks(answer);\n        foreach (var chunk in chunks)",
            StringComparison.Ordinal);
        inOneChunk.ShouldNotBe(streamed);
        project.Write("src/Host/Agents/StandInChatClient.cs", inOneChunk);
        return project;
    }

    /// <summary>The agent panel's ADR, whatever number the generated project gives it.</summary>
    private static string AgentUiAdr(Dictionary<string, string> files) =>
        files[files.Keys.Single(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-agent-ui-ag-ui.md", StringComparison.Ordinal))];

    /// <summary>The rule of every error eslint's stylish output lists (<c>  4:26  error  message  rule</c>), in order.</summary>
    private static string LintFindings(string output) =>
        string.Join(", ", output.Split('\n')
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.Length > 2 && words[0].Contains(':', StringComparison.Ordinal) && words[0].All(c => char.IsDigit(c) || c == ':') && words[1] == "error")
            .Select(words => words[^1]));

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

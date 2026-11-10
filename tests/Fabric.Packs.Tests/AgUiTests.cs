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

        files["web/app/package.json"].ShouldContain("\"@ag-ui/client\": \"1.0.3\"");
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

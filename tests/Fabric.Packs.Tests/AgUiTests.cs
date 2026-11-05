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
    public void No_profile_changes_because_none_has_agents()
    {
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var files = Files(profile);

            files.Keys.ShouldNotContain(path => path.Contains("AgentUi", StringComparison.Ordinal) || path.Contains("Assistant", StringComparison.Ordinal), profile);
            files["src/AppHost/AppHost.cs"].ShouldNotContain("model-stand-in", customMessage: profile);
            files["src/ServiceDefaults/Wiring/Extensions.cs"].ShouldNotContain("AGUI", customMessage: profile);
            files["fabric.yaml"].ShouldNotContain("assistant", customMessage: profile);
        }
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

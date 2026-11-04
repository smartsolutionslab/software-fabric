using System.Net;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm: ollama</c> (ADR-0010): one <c>IChatClient</c> for a local model served by Ollama, from the community
/// <c>OllamaSharp</c> package, registered in infrastructure with logging and OpenTelemetry. The AppHost runs Ollama with
/// <c>CommunityToolkit.Aspire.Hosting.Ollama</c>, with a data volume, and pulls <c>llama3.2:1b</c> at start; there is
/// no key. The parts every provider shares come from the base unchanged, as they do for openai. No test here pulls the
/// Ollama image or a model, or calls one: the integration-test fixture removes the Ollama resources and gives the host
/// an endpoint nothing listens on.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class OllamaTests
{
    private const string Profile = "lean-crud";
    private const string Ollama = "backend.llm=ollama";
    private const string Model = "llama3.2:1b";
    private const string DatabasePassword = "fabric-tests";

    /// <summary>An endpoint nothing listens on (the discard port); nothing is ever sent to it.</summary>
    private const string DeadEndpoint = "http://localhost:9";

    /// <summary>A key-shaped value; Ollama takes no key, and it never leaves the test process.</summary>
    private const string NotAKey = "not-a-real-key";

    private static readonly string[] SharedModelFiles =
    [
        "tests/Fakes/FakeChatClient.cs",
        "tests/Architecture.Tests/Rules/ProviderSdksStayOutOfDomainAndApplication.cs",
        "tests/Architecture.Tests/Rules/NoApiKeyInSettings.cs",
        "src/BuildingBlocks/Llm/Wiring/ChatClientRegistration.cs",
    ];

    private const string ModelProbe = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.AI;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api;

        public sealed class ModelProbe : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/model-probe", (IChatClient client) =>
                {
                    var metadata = client.GetService<ChatClientMetadata>();
                    return TypedResults.Ok($"{metadata?.ProviderName} {metadata?.DefaultModelId} {metadata?.ProviderUri}");
                });
        }
        """;

    /// <summary>Runs inside the generated integration tests: the model server is not in the running application, and the host still resolves its client, at the fixture's dead endpoint.</summary>
    private const string WithoutTheModelServer = """
        using Aspire.Hosting.ApplicationModel;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Integration.Tests;

        public class WithoutTheModelServerTests(AspireFixture aspire)
        {
            [Fact]
            public void The_ollama_resources_are_not_part_of_the_test_run()
            {
                var names = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
                    .Select(resource => resource.Name)
                    .ToList();

                names.ShouldNotContain("ollama");
                names.ShouldNotContain("ollama-model");
                names.ShouldContain("host");
            }

            [Fact]
            public async Task The_host_resolves_the_ollama_client_at_an_endpoint_nothing_listens_on()
            {
                using var client = aspire.CreateHostClient();

                var probe = await client.GetStringAsync(new Uri("/billing/model-probe", UriKind.Relative), TestContext.Current.CancellationToken);

                probe.ShouldBe("\"ollama llama3.2:1b http://localhost:9/\"");
            }
        }
        """;

    private const string ApplicationCallingTheSdk = """
        using OllamaSharp;

        namespace Shop.Modules.Billing.Application;

        public sealed class DirectOllama
        {
            public OllamaApiClient Client { get; } = new(new Uri("http://localhost:9"));
        }
        """;

    [Fact]
    public void A_project_calling_ollama_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Ollama]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_apphost_runs_ollama_with_a_data_volume_pulls_llama3_2_1b_and_the_host_waits_for_it()
    {
        var files = Files(Profile, Ollama);

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("builder.AddOllama(\"ollama\")");
        appHost.ShouldContain(".WithDataVolume()");
        appHost.ShouldContain($"ollama.AddModel(\"ollama-model\", \"{Model}\")");
        appHost.ShouldContain(".WithEnvironment(\"Ollama__Endpoint\", ollama.Resource.PrimaryEndpoint)");
        appHost.ShouldContain(".WithEnvironment(\"Ollama__Model\", ollamaModel.Resource.ModelName)");
        appHost.ShouldContain(".WaitFor(ollamaModel)");
        appHost.ShouldNotContain("secret: true");
        appHost.ShouldNotContain("WithGPUSupport(");
        files["src/AppHost/AppHost.csproj"].ShouldContain("<PackageReference Include=\"CommunityToolkit.Aspire.Hosting.Ollama\" />");
        var packages = files["Directory.Packages.props"];
        packages.ShouldContain("<PackageVersion Include=\"OllamaSharp\" Version=\"5.5.0\" />");
        packages.ShouldContain("<PackageVersion Include=\"CommunityToolkit.Aspire.Hosting.Ollama\" Version=\"13.6.1\" />");
        packages.ShouldContain("<PackageVersion Include=\"Microsoft.Extensions.AI\" Version=\"10.11.0\" />");
        packages.ShouldNotContain("Microsoft.Extensions.AI.Ollama");
        packages.ShouldNotContain("CommunityToolkit.Aspire.OllamaSharp");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldContain("<PackageReference Include=\"OllamaSharp\" />");
        files["src/BuildingBlocks/Llm/Wiring/OllamaChatClient.cs"].ShouldContain("new OllamaApiClient(");
        files.Keys.ShouldNotContain("src/BuildingBlocks/Llm/Wiring/OpenAIChatClient.cs");
        files.Keys.ShouldNotContain("src/BuildingBlocks/Llm/Wiring/AnthropicChatClient.cs");
        files.Where(file => Path.GetFileName(file.Key).StartsWith("appsettings", StringComparison.Ordinal))
            .ShouldAllBe(file => !file.Value.Contains("ollama", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_integration_test_fixture_removes_the_ollama_resources_and_points_the_host_at_a_dead_endpoint()
    {
        var fixture = Files(Profile, Ollama)["tests/Integration.Tests/AspireFixture.cs"];

        fixture.ShouldContain("builder.Resources.Remove(");
        fixture.ShouldContain("\"ollama\"");
        fixture.ShouldContain("\"ollama-model\"");
        fixture.ShouldContain("WaitAnnotation");
        fixture.ShouldContain($".WithEnvironment(\"Ollama__Endpoint\", \"{DeadEndpoint}\")");
        fixture.IndexOf("builder.Resources.Remove(", StringComparison.Ordinal)
            .ShouldBeLessThan(fixture.IndexOf("await builder.BuildAsync(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_shared_model_parts_come_from_the_base_and_no_profile_without_a_model_gets_them()
    {
        var files = Files(Profile, Ollama);
        var ollamaTargets = Repository.Packs.Packs["backend.llm.ollama"].Files.Select(file => file.Target).ToList();

        foreach (var shared in SharedModelFiles)
        {
            files.Keys.ShouldContain(shared);
            ollamaTargets.ShouldNotContain(shared);
        }

        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
            generated.Files.Select(file => file.Path).ShouldNotContain(path => SharedModelFiles.Contains(path), profile);
        }
    }

    [Fact]
    public void A_project_calling_ollama_builds_and_the_sample_query_answers_with_the_stand_in_model()
    {
        using var project = NewProject(Ollama);
        project.AddModule("billing");
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Modules/Billing.Tests/Queries/AskTheModelTests.cs"))
            .ShouldContain("FakeChatClient");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public async Task The_host_starts_with_the_ollama_client_registered_and_resolves_it_without_calling_the_model()
    {
        using var project = NewProject(Ollama, "backend.auth=none");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/ModelProbe.cs", ModelProbe);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["Ollama__Endpoint"] = DeadEndpoint,
            ["Ollama__Model"] = Model,
        });

        using var response = await host.GetAsync("/billing/model-probe");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldBe($"\"ollama {Model} {DeadEndpoint}/\"");
    }

    [Fact]
    public void The_integration_tests_pass_without_the_ollama_container_or_a_model()
    {
        using var project = NewProject(Ollama, "backend.auth=none");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/ModelProbe.cs", ModelProbe);
        project.Write("tests/Integration.Tests/WithoutTheModelServerTests.cs", WithoutTheModelServer);
        project.Build();

        // The probe is a new endpoint, so the frontend's OpenAPI copy is stale and its own test says so, by design
        // (LeanCrudSignInTests covers that); every other test runs.
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-not-class", "Shop.Integration.Tests.OpenApiDocumentTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_provider_sdk_in_application_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Ollama);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/DirectOllama.cs", ApplicationCallingTheSdk);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application references OllamaSharp;");
    }

    [Fact]
    public void An_api_key_in_a_settings_file_fails_the_architecture_tests_naming_the_file_and_the_key()
    {
        using var project = NewProject(Ollama);
        project.Write("src/Host/appsettings.Development.json", $$"""
            {
              "Ollama": {
                "ApiKey": "{{NotAKey}}"
              }
            }
            """);
        project.Write("src/AppHost/appsettings.Development.json", """
            {
              "Parameters": {
                "llm": "sk-proj-not-a-real-key"
              }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Host/appsettings.Development.json: 'Ollama:ApiKey' holds an API key");
        output.ShouldContain("src/AppHost/appsettings.Development.json: 'Parameters:llm' holds an API key");
        output.ShouldNotContain(NotAKey);
        output.ShouldNotContain("sk-proj-not-a-real-key");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A lean-crud project calling Ollama; fails while any of its options is generated by a stand-in.</summary>
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

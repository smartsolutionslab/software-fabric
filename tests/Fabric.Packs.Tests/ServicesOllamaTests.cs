using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017) with Ollama as the chat model or the embeddings provider (ADR-0010,
/// ADR-0012), and OpenAI embeddings: every service host, not the edge, calls the model, so the integration-test fixture
/// takes the dropped Ollama resources' waits off every service host, found by the AppHost's own convention
/// (<c>builder.ServiceHosts()</c>), and points each of them at the stand-in endpoint. A wait left on a removed resource
/// never ends, and the fixture is cancelled after its five-minute timeout (#815). No test here pulls Ollama or calls a model.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ServicesOllamaTests
{
    private const string Profile = "saas-standard-react";
    private const string Fixture = "tests/Integration.Tests/AspireFixture.cs";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none", "backend.auth=none"];

    /// <summary>An endpoint of the billing module answering which chat client its service host resolved.</summary>
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

    /// <summary>An endpoint of the billing module answering which embedding generator its service host resolved.</summary>
    private const string EmbeddingsProbe = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.AI;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api;

        public sealed class EmbeddingsProbe : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/embeddings-probe", (IEmbeddingGenerator<string, Embedding<float>> generator) =>
                {
                    var metadata = generator.GetService<EmbeddingGeneratorMetadata>();
                    return TypedResults.Ok($"{metadata?.ProviderName} {metadata?.DefaultModelId} {metadata?.ProviderUri}");
                });
        }
        """;

    /// <summary>Runs inside the generated integration tests: no Ollama resource runs, and no service host waits on one.</summary>
    private const string WithoutTheModelServer = """
        using Aspire.Hosting.ApplicationModel;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Integration.Tests;

        public class WithoutTheModelServerTests(AspireFixture aspire)
        {
            [Fact]
            public void No_ollama_resource_runs_and_no_service_host_waits_on_one()
            {
                var resources = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources;

                resources.Select(resource => resource.Name).ShouldNotContain(name => name.StartsWith("ollama", StringComparison.Ordinal));
                resources.Select(resource => resource.Name).ShouldContain("billing");
                resources.Select(resource => resource.Name).ShouldContain("shipping");
                resources.OfType<ProjectResource>()
                    .SelectMany(resource => resource.Annotations.OfType<WaitAnnotation>(), (resource, wait) => $"{resource.Name} -> {wait.Resource.Name}")
                    .ShouldNotContain(wait => wait.Contains("-> ollama", StringComparison.Ordinal));
            }

            [Fact]
            public async Task The_billing_service_resolves_its_model_at_the_fixtures_endpoint()
            {
                using var client = aspire.CreateHostClient();

                var probe = await client.GetStringAsync(new Uri("/billing/PROBE", UriKind.Relative), TestContext.Current.CancellationToken);

                probe.ShouldBe(EXPECTED);
            }
        }
        """;

    [Theory]
    [InlineData("xunit")]
    [InlineData("nunit")]
    [InlineData("tunit")]
    public void With_services_the_ollama_stand_in_takes_the_waits_off_every_service_host_and_gives_each_the_dead_endpoint(string framework)
    {
        var fixture = Files("backend.llm=ollama", $"backend.test.framework={framework}")[Fixture];

        fixture.ShouldContain("builder.ServiceHosts()");
        fixture.ShouldNotContain("resource.Name == \"host\"");
        fixture.ShouldContain(".WithEnvironment(\"Ollama__Endpoint\", \"http://localhost:9\")");
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("ollama")]
    public void With_services_the_ollama_embeddings_stand_in_takes_the_waits_off_every_service_host_and_gives_each_the_stand_in(string llm)
    {
        var fixture = Files($"backend.llm={llm}", "backend.llm.extras=rag", "backend.llm.embeddings=ollama")[Fixture];

        fixture.ShouldContain("builder.ServiceHosts()");
        fixture.ShouldNotContain("resource.Name == \"host\"");
        fixture.ShouldContain(".WithEnvironment(\"Ollama__Endpoint\", embeddings.Endpoint)");
        fixture.ShouldContain("\"ollama-embedding-model\"");
    }

    [Fact]
    public void With_services_openai_embeddings_point_every_service_host_at_the_stand_in()
    {
        var fixture = Files("backend.llm=anthropic", "backend.llm.extras=rag", "backend.llm.embeddings=openai")[Fixture];

        fixture.ShouldContain("builder.ServiceHosts()");
        fixture.ShouldNotContain("resource.Name == \"host\"");
        fixture.ShouldContain(".WithEnvironment(\"OpenAI__EmbeddingEndpoint\", embeddings.Endpoint)");
    }

    [Fact]
    public void A_services_project_calling_ollama_passes_its_integration_tests_with_no_service_host_waiting_on_ollama()
    {
        using var project = new GeneratedProject(Profile, [.. Services, "backend.llm=ollama"]);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Modules/Billing/Billing.Api/ModelProbe.cs", ModelProbe);
        project.Write("tests/Integration.Tests/WithoutTheModelServerTests.cs", Probing("model-probe", "\"\\\"ollama llama3.2:1b http://localhost:9/\\\"\""));
        project.Build();

        var output = IntegrationTests(project);

        output.ShouldNotContain("Waiting for resource 'ollama");
    }

    [Fact]
    public void A_services_project_embedding_with_ollama_passes_its_integration_tests_with_no_service_host_waiting_on_ollama()
    {
        using var project = new GeneratedProject(Profile, [.. Services, "backend.llm=openai", "backend.llm.extras=rag", "backend.llm.embeddings=ollama"]);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Modules/Billing/Billing.Api/EmbeddingsProbe.cs", EmbeddingsProbe);
        project.Write("tests/Integration.Tests/WithoutTheModelServerTests.cs", Probing("embeddings-probe", "$\"\\\"ollama nomic-embed-text {aspire.Embeddings.Endpoint}/\\\"\""));
        project.Build();

        var output = IntegrationTests(project);

        output.ShouldNotContain("Waiting for resource 'ollama");
    }

    private static string Probing(string probe, string expected) =>
        WithoutTheModelServer.Replace("PROBE", probe, StringComparison.Ordinal).Replace("EXPECTED", expected, StringComparison.Ordinal);

    /// <summary>Every integration test but the OpenAPI copy's, which the probe, a new endpoint, makes stale by design.</summary>
    private static string IntegrationTests(GeneratedProject project)
    {
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-not-class", "Shop.Integration.Tests.OpenApiDocumentTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        return output;
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [.. Services, .. overrides]);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

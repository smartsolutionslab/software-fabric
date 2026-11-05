using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm.embeddings</c> (ADR-0012): <c>openai</c>, <c>azure-openai</c> and <c>ollama</c> each register the
/// project's <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> in infrastructure, with logging and
/// OpenTelemetry, and default models: <c>text-embedding-3-small</c> for OpenAI and as Azure's deployment,
/// <c>nomic-embed-text</c> for Ollama. When the embeddings provider is the chat model's, the pack reuses the chat
/// pack's key and endpoint parameters and its package versions; otherwise it declares them itself (one owner per
/// version). The dimension is active only with <c>backend.llm.extras: rag</c>, so every project here chooses it, and
/// is generated from the real packs alone. No test here calls a model.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class EmbeddingsTests
{
    private const string Profile = "lean-crud";
    private const string Rag = "backend.llm.extras=rag";
    private const string Wiring = "src/BuildingBlocks/Llm/Wiring";

    /// <summary>An endpoint nothing listens on (the discard port); nothing is ever sent to it.</summary>
    private const string DeadEndpoint = "http://localhost:9";

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

    /// <summary>Runs inside the generated integration tests: no Ollama resource runs, and the host still resolves its embedding generator at the fixture's dead endpoint.</summary>
    private const string WithoutTheEmbeddingsServer = """
        using Aspire.Hosting.ApplicationModel;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Integration.Tests;

        public class WithoutTheEmbeddingsServerTests(AspireFixture aspire)
        {
            [Fact]
            public void The_ollama_resources_are_not_part_of_the_test_run()
            {
                var names = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
                    .Select(resource => resource.Name)
                    .ToList();

                names.ShouldNotContain("ollama");
                names.ShouldNotContain("ollama-model");
                names.ShouldNotContain("ollama-embedding-model");
                names.ShouldContain("host");
            }

            [Fact]
            public async Task The_host_resolves_the_ollama_embedding_generator_at_an_endpoint_nothing_listens_on()
            {
                using var client = aspire.CreateHostClient();

                var probe = await client.GetStringAsync(new Uri("/billing/embeddings-probe", UriKind.Relative), TestContext.Current.CancellationToken);

                probe.ShouldBe("\"ollama nomic-embed-text http://localhost:9/\"");
            }
        }
        """;

    public static TheoryData<string, string> EveryPairing { get; } = Pairings();

    [Theory]
    [MemberData(nameof(EveryPairing))]
    public void Every_embeddings_provider_with_rag_has_a_real_pack_so_nothing_stands_in(string llm, string embeddings)
    {
        using var standIns = new StandIns();

        standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, Overrides(llm, embeddings));

        standIns.StoodIn.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("azure-openai", "AzureOpenAI")]
    [InlineData("ollama", "Ollama")]
    public void The_host_registers_the_providers_embedding_generator_from_its_own_wiring_file(string embeddings, string type)
    {
        foreach (var llm in new[] { "anthropic", embeddings })
        {
            var files = Files(llm, embeddings);

            files["src/Host/Program.cs"].ShouldContain($"builder.Services.Add{type}EmbeddingGenerator(builder.Configuration);", Case.Sensitive, llm);
            files[$"{Wiring}/{type}EmbeddingGenerator.cs"].ShouldContain($"Add{type}EmbeddingGenerator(this IServiceCollection services, IConfiguration configuration)");
            files.Keys.ShouldContain($"tests/Architecture.Tests/Rules/{type}GeneratesTheProjectsEmbeddings.cs");
            files.Keys.Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith("EmbeddingGenerator.cs", StringComparison.Ordinal)).ShouldBe([$"{Wiring}/{type}EmbeddingGenerator.cs"]);
        }
    }

    [Theory]
    [MemberData(nameof(EveryPairing))]
    public void Every_package_version_parameter_and_apphost_variable_has_one_owner(string llm, string embeddings)
    {
        var files = Files(llm, embeddings);

        Duplicates(files["Directory.Packages.props"], "<PackageVersion Include=\"([^\"]+)\"").ShouldBeEmpty();
        Duplicates(files["src/BuildingBlocks/BuildingBlocks.csproj"], "<PackageReference Include=\"([^\"]+)\"").ShouldBeEmpty();
        Duplicates(files["src/AppHost/AppHost.csproj"], "<PackageReference Include=\"([^\"]+)\"").ShouldBeEmpty();
        var appHost = files["src/AppHost/AppHost.cs"];
        Duplicates(appHost, "AddParameter\\(\"([^\"]+)\"").ShouldBeEmpty();
        Duplicates(appHost, "var (\\w+) =").ShouldBeEmpty();
        Duplicates(appHost, "\\.WithEnvironment\\(\"(\\w+)\"").ShouldBeEmpty();
        Duplicates(files["tests/Integration.Tests/AspireFixture.cs"], "Configuration\\[\"(Parameters:[^\"]+)\"\\]").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("azure-openai")]
    [InlineData("ollama")]
    public void OpenAI_embeddings_default_to_text_embedding_3_small_and_need_the_openai_key(string llm)
    {
        var files = Files(llm, "openai");

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("var openAIEmbeddingModel = builder.AddParameter(\"openai-embedding-model\", \"text-embedding-3-small\", publishValueAsDefault: true);");
        appHost.ShouldContain(".WithEnvironment(\"OpenAI__EmbeddingModel\", openAIEmbeddingModel)");
        appHost.ShouldContain("builder.AddParameter(\"openai-api-key\", secret: true)");
        appHost.ShouldContain(".WithEnvironment(\"OpenAI__ApiKey\", openAIApiKey)");
        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Microsoft.Extensions.AI.OpenAI\" Version=\"10.11.0\" />");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldContain("<PackageReference Include=\"Microsoft.Extensions.AI.OpenAI\" />");
        files["tests/Integration.Tests/AspireFixture.cs"].ShouldContain("builder.Configuration[\"Parameters:openai-api-key\"] = \"not-a-real-key\";");
        files[$"{Wiring}/OpenAIEmbeddingGenerator.cs"].ShouldContain(".GetEmbeddingClient(");
        files["README.md"].ShouldContain("openai-api-key");
    }

    [Theory]
    [InlineData("azure-openai")]
    [InlineData("anthropic")]
    [InlineData("openai")]
    [InlineData("ollama")]
    public void Azure_embeddings_default_to_a_text_embedding_3_small_deployment_at_the_chat_packs_endpoint_or_their_own(string llm)
    {
        var files = Files(llm, "azure-openai");

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("var azureOpenAIEmbeddingDeployment = builder.AddParameter(\"azure-openai-embedding-deployment\", \"text-embedding-3-small\", publishValueAsDefault: true);");
        appHost.ShouldContain(".WithEnvironment(\"AzureOpenAI__EmbeddingDeployment\", azureOpenAIEmbeddingDeployment)");
        appHost.ShouldContain("builder.AddParameter(\"azure-openai-endpoint\")");
        appHost.ShouldContain("builder.AddParameter(\"azure-openai-api-key\", secret: true)");
        appHost.ShouldContain(".WithEnvironment(\"AzureOpenAI__Endpoint\", azureOpenAIEndpoint)");
        appHost.ShouldContain(".WithEnvironment(\"AzureOpenAI__ApiKey\", azureOpenAIApiKey)");
        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Microsoft.Extensions.AI.OpenAI\" Version=\"10.11.0\" />");
        var fixture = files["tests/Integration.Tests/AspireFixture.cs"];
        fixture.ShouldContain("builder.Configuration[\"Parameters:azure-openai-endpoint\"] = \"https://not-a-real-resource.openai.azure.com\";");
        fixture.ShouldContain("builder.Configuration[\"Parameters:azure-openai-api-key\"] = \"not-a-real-key\";");
        files[$"{Wiring}/AzureOpenAIEmbeddingGenerator.cs"].ShouldContain("/openai/v1/");
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("anthropic")]
    [InlineData("openai")]
    public void Ollama_embeddings_pull_nomic_embed_text_on_the_one_ollama_server_and_the_fixture_removes_it(string llm)
    {
        var files = Files(llm, "ollama");

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("var ollama = builder.AddOllama(\"ollama\")");
        appHost.ShouldContain("var ollamaEmbeddingModel = ollama.AddModel(\"ollama-embedding-model\", \"nomic-embed-text\");");
        appHost.ShouldContain(".WithEnvironment(\"Ollama__Endpoint\", ollama.Resource.PrimaryEndpoint)");
        appHost.ShouldContain(".WithEnvironment(\"Ollama__EmbeddingModel\", ollamaEmbeddingModel.Resource.ModelName)");
        appHost.ShouldContain(".WaitFor(ollamaEmbeddingModel)");
        appHost.IndexOf("var ollama = ", StringComparison.Ordinal).ShouldBeLessThan(appHost.IndexOf("var ollamaEmbeddingModel = ", StringComparison.Ordinal));
        files["src/AppHost/AppHost.csproj"].ShouldContain("<PackageReference Include=\"CommunityToolkit.Aspire.Hosting.Ollama\" />");
        var packages = files["Directory.Packages.props"];
        packages.ShouldContain("<PackageVersion Include=\"OllamaSharp\" Version=\"5.5.0\" />");
        packages.ShouldContain("<PackageVersion Include=\"CommunityToolkit.Aspire.Hosting.Ollama\" Version=\"13.6.1\" />");
        var fixture = files["tests/Integration.Tests/AspireFixture.cs"];
        fixture.ShouldContain("\"ollama-embedding-model\"");
        fixture.ShouldContain($".WithEnvironment(\"Ollama__Endpoint\", \"{DeadEndpoint}\")");
        fixture.LastIndexOf("builder.Resources.Remove(", StringComparison.Ordinal)
            .ShouldBeLessThan(fixture.IndexOf("await builder.BuildAsync(", StringComparison.Ordinal));
        files["README.md"].ShouldContain("nomic-embed-text");
    }

    [Fact]
    public void No_profile_gets_an_embeddings_part()
    {
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);

            generated.Errors.ShouldBeEmpty(profile);
            generated.Files.ShouldAllBe(file => !file.Content.Contains("EmbeddingGenerator", StringComparison.Ordinal), profile);
        }
    }

    [Theory]
    [InlineData("openai", "openai", "OpenAI")]
    [InlineData("anthropic", "openai", "OpenAI")]
    [InlineData("azure-openai", "azure-openai", "AzureOpenAI")]
    [InlineData("openai", "azure-openai", "AzureOpenAI")]
    [InlineData("ollama", "ollama", "Ollama")]
    [InlineData("anthropic", "ollama", "Ollama")]
    public void A_project_with_rag_builds_and_its_architecture_tests_fail_once_the_host_stops_registering_the_generator(string llm, string embeddings, string type)
    {
        using var project = NewProject(llm, embeddings);
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        var program = Path.Combine(project.ProjectDirectory, "src/Host/Program.cs");
        var registration = $"builder.Services.Add{type}EmbeddingGenerator(builder.Configuration);";
        File.ReadAllText(program).ShouldContain(registration);
        File.WriteAllText(program, File.ReadAllText(program).Replace(registration, string.Empty, StringComparison.Ordinal));

        var (unregisteredExitCode, unregisteredOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        unregisteredExitCode.ShouldNotBe(0, unregisteredOutput);
        unregisteredOutput.ShouldContain($"src/Host/Program.cs never calls Add{type}EmbeddingGenerator");
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("anthropic")]
    public void The_integration_tests_pass_without_an_ollama_container_or_the_embedding_model(string llm)
    {
        using var project = NewProject(llm, "ollama", "backend.auth=none");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/EmbeddingsProbe.cs", EmbeddingsProbe);
        project.Write("tests/Integration.Tests/WithoutTheEmbeddingsServerTests.cs", WithoutTheEmbeddingsServer);
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

    private static TheoryData<string, string> Pairings()
    {
        TheoryData<string, string> pairings = [];
        foreach (var llm in new[] { "openai", "azure-openai", "ollama", "anthropic" })
        {
            foreach (var embeddings in new[] { "openai", "azure-openai", "ollama" })
            {
                pairings.Add(llm, embeddings);
            }
        }

        return pairings;
    }

    private static string[] Overrides(string llm, string embeddings, params string[] more) =>
        [$"backend.llm={llm}", Rag, $"backend.llm.embeddings={embeddings}", .. more];

    private static List<string> Duplicates(string content, string pattern) =>
        [.. Regex.Matches(content, pattern)
            .GroupBy(match => match.Groups[1].Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

    /// <summary>The project's files, generated from the real packs alone.</summary>
    private static Dictionary<string, string> Files(string llm, string embeddings)
    {
        using var standIns = new StandIns();
        var overrides = Overrides(llm, embeddings);
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);
        standIns.StoodIn.ShouldBeEmpty();

        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A lean-crud project with rag; fails while any option is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string llm, string embeddings, params string[] more)
    {
        var project = new GeneratedProject(Profile, Overrides(llm, embeddings, more));
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count != 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty(string.Join(", ", standIns));
        return project;
    }
}

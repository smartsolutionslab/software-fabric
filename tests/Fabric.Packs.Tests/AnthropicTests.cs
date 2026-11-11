using System.Net;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm: anthropic</c> (ADR-0010): one <c>IChatClient</c> for Claude, from the official <c>Anthropic</c>
/// package, registered in infrastructure with logging and OpenTelemetry; the key an Aspire secret parameter, the model
/// a parameter defaulting to <c>claude-haiku-5-5</c>. The parts every provider shares (the tests' stand-in model, the
/// rule keeping provider SDKs out of Domain and Application, the API-key gate and the sample query) come from the base,
/// for projects that call a model only. No test here calls the Anthropic API.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class AnthropicTests
{
    private const string Profile = "lean-crud";
    private const string Anthropic = "backend.llm=anthropic";
    private const string DatabasePassword = "fabric-tests";

    /// <summary>A key-shaped value no provider accepts; it never leaves the test process and the host it starts.</summary>
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
                    return TypedResults.Ok($"{metadata?.ProviderName} {metadata?.DefaultModelId}");
                });
        }
        """;

    private const string ApplicationCallingTheSdk = """
        using Anthropic;

        namespace Shop.Modules.Billing.Application;

        public sealed class DirectClaude
        {
            public IAnthropicClient Client { get; } = new AnthropicClient();
        }
        """;

    [Fact]
    public void A_project_calling_claude_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Anthropic]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_key_is_an_aspire_secret_parameter_and_the_model_a_parameter_defaulting_to_claude_haiku()
    {
        var files = Files(Profile, Anthropic);

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("builder.AddParameter(\"anthropic-api-key\", secret: true)");
        appHost.ShouldContain("builder.AddParameter(\"anthropic-model\", \"claude-haiku-5-5\", publishValueAsDefault: true)");
        appHost.ShouldContain(".WithEnvironment(\"Anthropic__ApiKey\", anthropicApiKey)");
        appHost.ShouldContain(".WithEnvironment(\"Anthropic__Model\", anthropicModel)");
        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Anthropic\" Version=\"13.8.0\" />");
        files["src/BuildingBlocks/Llm/Wiring/AnthropicChatClient.cs"].ShouldContain(".AsIChatClient(");
        files.Where(file => Path.GetFileName(file.Key).StartsWith("appsettings", StringComparison.Ordinal))
            .ShouldAllBe(file => !file.Value.Contains("anthropic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_shared_model_parts_come_from_the_base_and_no_profile_without_a_model_gets_them()
    {
        var files = Files(Profile, Anthropic);
        var anthropicTargets = Repository.Packs.Packs["backend.llm.anthropic"].Files.Select(file => file.Target).ToList();

        foreach (var shared in SharedModelFiles)
        {
            files.Keys.ShouldContain(shared);
            anthropicTargets.ShouldNotContain(shared);
        }

        var withoutAModel = Repository.ProfilesChoosing("backend.llm", "none");
        var withAModel = Repository.ProfilesNotChoosing("backend.llm", "none");
        withoutAModel.ShouldNotBeEmpty();
        withAModel.ShouldNotBeEmpty();
        foreach (var profile in withoutAModel)
        {
            var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
            generated.Files.Select(file => file.Path).ShouldNotContain(path => SharedModelFiles.Contains(path), profile);
        }

        foreach (var profile in withAModel)
        {
            var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
            var paths = generated.Files.Select(file => file.Path).ToList();
            foreach (var shared in SharedModelFiles)
            {
                paths.ShouldContain(shared, profile);
            }
        }
    }

    [Fact]
    public void A_project_calling_claude_builds_and_the_sample_query_answers_with_the_stand_in_model()
    {
        using var project = NewProject(Anthropic);
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
    public async Task The_host_starts_with_the_anthropic_client_registered_and_resolves_it_without_calling_the_model()
    {
        using var project = NewProject(Anthropic, "backend.auth=none");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/ModelProbe.cs", ModelProbe);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["Anthropic__ApiKey"] = NotAKey,
            ["Anthropic__Model"] = "claude-haiku-5-5",
        });

        using var response = await host.GetAsync("/billing/model-probe");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldBe("\"anthropic claude-haiku-5-5\"");
    }

    [Fact]
    public void The_integration_tests_pass_with_the_fixture_supplying_the_key_parameter()
    {
        using var project = NewProject(Anthropic, "backend.auth=none");
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_provider_sdk_in_application_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Anthropic);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/DirectClaude.cs", ApplicationCallingTheSdk);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application references Anthropic");
    }

    [Fact]
    public void An_api_key_in_a_settings_file_fails_the_architecture_tests_naming_the_file_and_the_key()
    {
        using var project = NewProject(Anthropic);
        project.Write("src/Host/appsettings.Development.json", $$"""
            {
              "Anthropic": {
                "ApiKey": "{{NotAKey}}"
              }
            }
            """);
        project.Write("src/AppHost/appsettings.Development.json", """
            {
              "Parameters": {
                "claude": "sk-ant-not-a-real-key"
              }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Host/appsettings.Development.json: 'Anthropic:ApiKey' holds an API key");
        output.ShouldContain("src/AppHost/appsettings.Development.json: 'Parameters:claude' holds an API key");
        output.ShouldNotContain(NotAKey);
        output.ShouldNotContain("sk-ant-not-a-real-key");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A lean-crud project calling Claude; fails while any of its options is generated by a stand-in.</summary>
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

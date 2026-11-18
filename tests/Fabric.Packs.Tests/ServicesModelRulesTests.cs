using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017) with a model (ADR-0010) and embeddings (ADR-0012): the rules that find
/// the provider's registration in the host's <c>Program.cs</c> check every service host's
/// <c>src/Services/&lt;Module&gt;/Host/Program.cs</c>, which gets the registration through the host.services slot, and
/// name each one that lacks it; there is no <c>src/Host</c>. A modular-monolith project's rule still checks
/// <c>src/Host/Program.cs</c>. No test here calls a model.
/// </summary>
public sealed class ServicesModelRulesTests
{
    private const string Profile = "saas-standard-react";
    private const string MonolithHostProgram = "src/Host/Program.cs";
    private const string ServiceHosts = "src/Services/";
    private const string Rag = "backend.llm.extras=rag";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none"];

    [Theory]
    [InlineData("anthropic", "Anthropic")]
    [InlineData("openai", "OpenAI")]
    [InlineData("azure-openai", "AzureOpenAI")]
    [InlineData("ollama", "Ollama")]
    public void With_services_the_model_rule_checks_the_service_hosts_program_and_not_src_Host(string llm, string type)
    {
        var files = WithModules([.. Services, $"backend.llm={llm}"], "billing", "shipping");
        var rule = files[$"tests/Architecture.Tests/Rules/{type}IsTheProjectsModel.cs"];

        foreach (var module in new[] { "Billing", "Shipping" })
        {
            files[$"src/Services/{module}/Host/Program.cs"].ShouldContain($"builder.Services.Add{type}ChatClient(builder.Configuration);");
        }

        rule.ShouldNotContain(MonolithHostProgram, customMessage: "with services there is no src/Host; the rule checks every service host.");
        rule.ShouldContain(ServiceHosts, customMessage: "the rule finds the service hosts' Program.cs under src/Services.");
        rule.ShouldContain($"Add{type}ChatClient");
        rule.ShouldNotContain("Billing", customMessage: "the rule finds the service hosts by convention, so add-module changes no rule.");
        rule.ShouldNotContain("Shipping", customMessage: "the rule finds the service hosts by convention, so add-module changes no rule.");
    }

    [Theory]
    [InlineData("anthropic", "Anthropic")]
    [InlineData("openai", "OpenAI")]
    [InlineData("azure-openai", "AzureOpenAI")]
    [InlineData("ollama", "Ollama")]
    public void With_a_modular_monolith_the_model_rule_still_checks_src_Host(string llm, string type)
    {
        var rule = WithModules(["backend.tenancy=none", $"backend.llm={llm}"], "billing")[$"tests/Architecture.Tests/Rules/{type}IsTheProjectsModel.cs"];

        rule.ShouldContain($"\"{MonolithHostProgram}\"");
        rule.ShouldNotContain(ServiceHosts);
    }

    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("azure-openai", "AzureOpenAI")]
    [InlineData("ollama", "Ollama")]
    public void With_services_the_embeddings_rule_checks_the_service_hosts_program_and_not_src_Host(string embeddings, string type)
    {
        var files = WithModules([.. Services, .. Embeddings(embeddings)], "billing", "shipping");
        var rule = files[$"tests/Architecture.Tests/Rules/{type}GeneratesTheProjectsEmbeddings.cs"];

        foreach (var module in new[] { "Billing", "Shipping" })
        {
            files[$"src/Services/{module}/Host/Program.cs"].ShouldContain($"builder.Services.Add{type}EmbeddingGenerator(builder.Configuration);");
        }

        rule.ShouldNotContain(MonolithHostProgram, customMessage: "with services there is no src/Host; the rule checks every service host.");
        rule.ShouldContain(ServiceHosts, customMessage: "the rule finds the service hosts' Program.cs under src/Services.");
        rule.ShouldContain($"Add{type}EmbeddingGenerator");
        rule.ShouldNotContain("Billing", customMessage: "the rule finds the service hosts by convention, so add-module changes no rule.");
        rule.ShouldNotContain("Shipping", customMessage: "the rule finds the service hosts by convention, so add-module changes no rule.");
    }

    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("azure-openai", "AzureOpenAI")]
    [InlineData("ollama", "Ollama")]
    public void With_a_modular_monolith_the_embeddings_rule_still_checks_src_Host(string embeddings, string type)
    {
        var rule = WithModules(["backend.tenancy=none", .. Embeddings(embeddings)], "billing")[$"tests/Architecture.Tests/Rules/{type}GeneratesTheProjectsEmbeddings.cs"];

        rule.ShouldContain($"\"{MonolithHostProgram}\"");
        rule.ShouldNotContain(ServiceHosts);
    }

    [Fact]
    public void A_services_project_calling_ollama_passes_its_architecture_tests_and_fails_them_naming_the_service_host_that_stops_registering_the_model()
    {
        using var project = NewProject([.. Services, "backend.llm=ollama"]);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        Unregister(project, "src/Services/Billing/Host/Program.cs", "builder.Services.AddOllamaChatClient(builder.Configuration);");

        var (unregisteredExitCode, unregisteredOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        unregisteredExitCode.ShouldNotBe(0, unregisteredOutput);
        unregisteredOutput.ShouldContain("src/Services/Billing/Host/Program.cs never calls AddOllamaChatClient");
        unregisteredOutput.ShouldNotContain("src/Services/Shipping/Host/Program.cs");
        unregisteredOutput.ShouldNotContain(MonolithHostProgram);
    }

    [Fact]
    public void A_services_project_with_ollama_embeddings_passes_its_architecture_tests_and_fails_them_naming_the_service_host_that_stops_registering_the_generator()
    {
        using var project = NewProject([.. Services, .. Embeddings("ollama")]);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        Unregister(project, "src/Services/Billing/Host/Program.cs", "builder.Services.AddOllamaEmbeddingGenerator(builder.Configuration);");

        var (unregisteredExitCode, unregisteredOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        unregisteredExitCode.ShouldNotBe(0, unregisteredOutput);
        unregisteredOutput.ShouldContain("src/Services/Billing/Host/Program.cs never calls AddOllamaEmbeddingGenerator");
        unregisteredOutput.ShouldNotContain("src/Services/Shipping/Host/Program.cs");
        unregisteredOutput.ShouldNotContain(MonolithHostProgram);
    }

    /// <summary>The chat model and the embeddings from one provider, with rag, the only extra that makes embeddings active.</summary>
    private static string[] Embeddings(string provider) =>
        [$"backend.llm={provider}", Rag, $"backend.llm.embeddings={provider}"];

    private static void Unregister(GeneratedProject project, string relativePath, string registration)
    {
        var path = Path.Combine(project.ProjectDirectory, relativePath);
        var content = File.ReadAllText(path);
        content.ShouldContain(registration);
        File.WriteAllText(path, content.Replace(registration, string.Empty, StringComparison.Ordinal));
    }

    /// <summary>The project's files from the real packs alone, with each module added as add-module adds it.</summary>
    private static Dictionary<string, string> WithModules(string[] overrides, params string[] modules)
    {
        using var standIns = new StandIns();
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);
        standIns.StoodIn.ShouldBeEmpty();

        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
            var addition = Generator.AddModule(Repository.Catalog, packs, record, module, []);
            addition.Errors.ShouldBeEmpty();
            foreach (var file in addition.Created)
            {
                files.Add(file.Path, file.Content);
            }

            files[FabricFile.FileName] = addition.FabricYaml!;
        }

        return files;
    }

    /// <summary>A saas-standard-react project with the overrides; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string[] overrides)
    {
        var project = new GeneratedProject(Profile, overrides);
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

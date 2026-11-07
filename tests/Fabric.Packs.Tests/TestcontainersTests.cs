using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// Integration tests on Testcontainers (<c>backend.test.integration.testcontainers</c>, #260): the application's
/// resources start in containers that Testcontainers runs, the host in the test process, and no test reaches the
/// application through Aspire's testing host. The AppHost stays the composition root for running the application.
/// </summary>
public sealed partial class TestcontainersTests
{
    private const string Testcontainers = "backend.test.integration=testcontainers";
    private const string StandIn = "This option's pack is not written yet.";
    private const string TestcontainersVersion = "4.16.0";

    /// <summary>The test projects that start containers: the application's, and rag's database alone.</summary>
    private static readonly string[] ContainerTests = ["tests/Integration.Tests/Integration.Tests.csproj", "tests/Rag.Tests/Rag.Tests.csproj"];

    public static TheoryData<string> Profiles => ["saas-standard-react", "saas-standard-vue", "industrial-event-driven-mfe", "lean-crud", "saas-strict-react", "saas-ai-react"];

    [Fact]
    public void A_project_with_testcontainers_starts_its_resources_in_containers_and_its_host_in_the_tests()
    {
        using var project = NewProject("saas-standard-react");
        project.AddModule("billing");

        var tests = Path.Combine(project.ProjectDirectory, "tests/Integration.Tests");
        File.Exists(Path.Combine(tests, "ContainerFixture.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(tests, "AspireFixture.cs")).ShouldBeFalse();
        var csproj = File.ReadAllText(Path.Combine(tests, "Integration.Tests.csproj"));
        csproj.ShouldContain("<PackageReference Include=\"Testcontainers\" />");
        csproj.ShouldContain("<PackageReference Include=\"Testcontainers.RabbitMq\" />");
        csproj.ShouldContain("<PackageReference Include=\"Testcontainers.Keycloak\" />");
        csproj.ShouldContain("<PackageReference Include=\"Microsoft.AspNetCore.Mvc.Testing\" />");
        csproj.ShouldContain("<ProjectReference Include=\"../../src/Host/Host.csproj\" />");
        var versions = File.ReadAllText(Path.Combine(project.ProjectDirectory, "Directory.Packages.props"));
        versions.ShouldContain($"<PackageVersion Include=\"Testcontainers\" Version=\"{TestcontainersVersion}\" />");
        versions.ShouldContain($"<PackageVersion Include=\"Testcontainers.RabbitMq\" Version=\"{TestcontainersVersion}\" />");
        versions.ShouldContain($"<PackageVersion Include=\"Testcontainers.Keycloak\" Version=\"{TestcontainersVersion}\" />");
        versions.ShouldNotContain("Aspire.Hosting.Testing");

        var fixture = File.ReadAllText(Path.Combine(tests, "ContainerFixture.cs"));
        fixture.ShouldContain("ConnectionStrings:database");
        fixture.ShouldContain("ConnectionStrings:messaging");
        fixture.ShouldContain("Authentication:Authority");
        fixture.ShouldContain("src/MigrationRunner");

        var named = AspireNamed(project, "tests");
        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    /// <summary>Every profile generates with Testcontainers, and none of its tests reaches the application through Aspire.</summary>
    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_profile_generates_with_testcontainers(string profile)
    {
        using var project = NewProject(profile);
        project.AddModule("billing");

        var named = AspireNamed(project, "tests");
        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    /// <summary>
    /// The MCP client's tests read the AppHost's model through Aspire's testing package whatever the integration option,
    /// so its version is declared, once, without the Aspire fixture too.
    /// </summary>
    [Theory]
    [InlineData("backend.test.integration=testcontainers")]
    [InlineData("backend.test.integration=aspire-fixture")]
    public void The_mcp_clients_tests_find_aspires_testing_package_with_either_integration_option(string integration)
    {
        using var project = new GeneratedProject("saas-ai-react", integration, "backend.mcp=client");

        var versions = File.ReadAllLines(Path.Combine(project.ProjectDirectory, "Directory.Packages.props"));
        versions.Count(line => line.Contains("<PackageVersion Include=\"Aspire.Hosting.Testing\"", StringComparison.Ordinal)).ShouldBe(1);
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Mcp.Tests/Mcp.Tests.csproj"))
            .ShouldContain("<PackageReference Include=\"Aspire.Hosting.Testing\" />");
    }

    /// <summary>
    /// Rag's tests take the database alone: its container and the MigrationRunner, and nothing else the application
    /// runs. The model providers' settings, which the AppHost passes the host as parameters, reach the in-process host.
    /// </summary>
    [Theory]
    [InlineData("openai", "openai", new[] { "OpenAI:ApiKey", "OpenAI:Model", "OpenAI:EmbeddingModel", "Agents:ModelStandIn" })]
    [InlineData("anthropic", "ollama", new[] { "Anthropic:ApiKey", "Anthropic:Model", "Ollama:Endpoint", "Ollama:EmbeddingModel", "Agents:ModelStandIn" })]
    [InlineData("azure-openai", "azure-openai", new[] { "AzureOpenAI:Endpoint", "AzureOpenAI:ApiKey", "AzureOpenAI:Deployment", "AzureOpenAI:EmbeddingDeployment", "Agents:ModelStandIn" })]
    [InlineData("ollama", "ollama", new[] { "Ollama:Endpoint", "Ollama:Model", "Ollama:EmbeddingModel", "Agents:ModelStandIn" })]
    public void The_model_providers_settings_reach_the_host_and_rags_database_starts_alone(string llm, string embeddings, string[] settings)
    {
        using var project = NewProject("saas-ai-react", $"backend.llm={llm}", $"backend.llm.embeddings={embeddings}");

        var fixture = File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Integration.Tests/ContainerFixture.cs"));
        foreach (var setting in settings)
        {
            fixture.ShouldContain($"settings[\"{setting}\"]", customMessage: setting);
        }

        var database = File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Rag.Tests/ApplicationDatabase.cs"));
        database.ShouldContain("pgvector/pgvector");
        database.ShouldContain("src/MigrationRunner");
        database.ShouldNotContain("keycloak", Case.Insensitive);
        database.ShouldNotContain("rabbitmq", Case.Insensitive);
        database.ShouldNotContain("settings[\"OpenAI", Case.Sensitive);
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Rag.Tests/Rag.Tests.csproj"))
            .ShouldContain("<PackageReference Include=\"Testcontainers\" />");
    }

    /// <summary>
    /// The generated integration tests pass on Testcontainers: PostgreSQL, the broker (RabbitMQ, Kafka or none) and
    /// Keycloak (or no identity provider) in containers, the host in process, every pack's integration tests against
    /// them, and rag's database tests against the database alone.
    /// </summary>
    [Theory]
    [InlineData("saas-standard-react", new string[0])]
    [InlineData("industrial-event-driven-mfe", new string[0])]
    [InlineData("lean-crud", new[] { "frontend.realtime=signalr", "backend.auth=none", "backend.tenancy=none" })]
    [InlineData("saas-ai-react", new[] { "backend.authorization=roles,claims", "backend.mcp=server" })]
    public void The_integration_tests_pass_on_testcontainers(string profile, string[] overrides)
    {
        using var project = NewProject(profile, overrides);
        // A profile that brings modules is tested with them. A module added afterwards would, with authorization, bring
        // its /permissions/manage endpoint, which makes the frontend's OpenAPI copy stale by design (RoleAuthorizationTests).
        if (!Directory.Exists(Path.Combine(project.ProjectDirectory, "src", "Modules")))
        {
            project.AddModule("billing");
        }

        project.Build();

        foreach (var tests in ContainerTests.Where(path => File.Exists(Path.Combine(project.ProjectDirectory, path))))
        {
            var (exitCode, output) = project.Test(tests);

            exitCode.ShouldBe(0, $"{tests}:{Environment.NewLine}{output}");
            output.ShouldContain("failed: 0", customMessage: tests);
            output.ShouldNotContain("succeeded: 0", customMessage: tests);
        }
    }

    /// <summary>The lines under the directory naming Aspire's testing host; the MCP client's tests read the AppHost's model and start nothing.</summary>
    private static List<string> AspireNamed(GeneratedProject project, string directory) =>
        [.. Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, directory), "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(path => (path, relative: Path.GetRelativePath(project.ProjectDirectory, path).Replace('\\', '/')))
            .Where(file => !file.relative.StartsWith("tests/Mcp.Tests/", StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file.path).Select((line, index) => (file.relative, line, index)))
            .Where(entry => Aspire().IsMatch(entry.line))
            .Select(entry => $"{entry.relative}:{entry.index + 1}: {entry.line.Trim()}")];

    /// <summary>A project with Testcontainers; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string profile, params string[] more)
    {
        var project = new GeneratedProject(profile, [Testcontainers, .. more]);
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains(StandIn, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count > 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty();
        return project;
    }

    [GeneratedRegex(@"\bAspireFixture\b|Aspire\.Hosting|AppHost\.csproj|DistributedApplication")]
    private static partial Regex Aspire();
}

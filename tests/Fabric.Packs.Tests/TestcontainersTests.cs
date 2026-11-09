using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// Integration tests on Testcontainers (<c>backend.test.integration.testcontainers</c>, #260): the application's
/// resources start in containers that Testcontainers runs, the host in the test process, and no test reaches the
/// application through Aspire's testing host. The AppHost stays the composition root for running the application.
/// </summary>
[Collection(DockerPorts.Name)]
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

    /// <summary>
    /// The Azure Service Bus emulator starts in a container of Testcontainers' module, the image the AppHost runs, and
    /// the host gets both its connection strings: messages, and the management requests Wolverine creates queues with.
    /// </summary>
    [Fact]
    public void The_service_bus_emulator_starts_in_a_container_and_the_host_gets_both_its_connection_strings()
    {
        using var project = NewProject("saas-standard-react", "backend.bus.transport=azure-service-bus");

        var tests = Path.Combine(project.ProjectDirectory, "tests/Integration.Tests");
        File.ReadAllText(Path.Combine(tests, "Integration.Tests.csproj")).ShouldContain("<PackageReference Include=\"Testcontainers.ServiceBus\" />");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "Directory.Packages.props"))
            .ShouldContain($"<PackageVersion Include=\"Testcontainers.ServiceBus\" Version=\"{TestcontainersVersion}\" />");
        var fixture = File.ReadAllText(Path.Combine(tests, "ContainerFixture.cs"));
        fixture.ShouldContain("servicebus-emulator:2.0.0");
        fixture.ShouldContain("settings[\"ConnectionStrings:messaging\"]");
        fixture.ShouldContain("settings[\"ConnectionStrings:messaging-management\"]");
        fixture.ShouldNotContain("RabbitMq", Case.Insensitive);
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
    /// The generated integration tests pass on Testcontainers: PostgreSQL, the broker (RabbitMQ, Kafka, the Azure Service Bus emulator or none) and
    /// Keycloak (or no identity provider) in containers, the host in process, every pack's integration tests against
    /// them, and rag's database tests against the database alone.
    /// </summary>
    [Theory]
    [InlineData("saas-standard-react", new string[0])]
    [InlineData("industrial-event-driven-mfe", new string[0])]
    [InlineData("saas-standard-react", new[] { "backend.bus.transport=azure-service-bus" })]
    [InlineData("lean-crud", new[] { "frontend.realtime=signalr", "backend.auth=none", "backend.tenancy=none" })]
    [InlineData("lean-crud", new[] { "backend.database=sqlserver" })]
    [InlineData("saas-ai-react", new[] { "backend.authorization=roles,claims", "backend.mcp=server" })]
    public void The_integration_tests_pass_on_testcontainers(string profile, string[] overrides)
    {
        using var project = NewProject(profile, overrides);
        // A profile that brings modules is tested with them.
        if (!Directory.EnumerateDirectories(Path.Combine(project.ProjectDirectory, "src", "Modules")).Any())
        {
            project.AddModule("billing");
        }

        project.Build();
        DockerPorts.EnsureRunsAlone();

        foreach (var tests in ContainerTests.Where(path => File.Exists(Path.Combine(project.ProjectDirectory, path))))
        {
            var (exitCode, output) = project.Test(tests);

            exitCode.ShouldBe(0, $"{tests}:{Environment.NewLine}{output}");
            output.ShouldContain("failed: 0", customMessage: tests);
            output.ShouldNotContain("succeeded: 0", customMessage: tests);
        }
    }

    /// <summary>
    /// A MigrationRunner that outlives the fixture's startup timeout is killed with the setup it fails, not left running
    /// (#314): the timeout comes from INTEGRATION_TESTS_STARTUP_TIMEOUT, here seconds, and the runner stalls forever
    /// after recording its process ID. <c>dotnet run</c> starts it as a child of its own, so only killing the whole
    /// process tree ends it. Both fixtures that run it are covered: the application's, and rag's database alone (#317).
    /// </summary>
    [Theory]
    [InlineData("lean-crud", "tests/Integration.Tests/Integration.Tests.csproj", new[] { "frontend.realtime=signalr", "backend.auth=none", "backend.tenancy=none" })]
    [InlineData("saas-ai-react", "tests/Rag.Tests/Rag.Tests.csproj", new string[0])]
    public void A_migration_runner_that_outlives_the_startup_timeout_is_killed_when_setup_fails(string profile, string tests, string[] overrides)
    {
        using var project = NewProject(profile, overrides);
        project.Write("src/MigrationRunner/Program.cs", """
            using System.Globalization;

            // Stalls: records its process ID, then never ends.
            var file = Environment.GetEnvironmentVariable("STALLING_RUNNER_PID_FILE") ?? throw new InvalidOperationException("No STALLING_RUNNER_PID_FILE.");
            await File.WriteAllTextAsync(file, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            await Task.Delay(Timeout.Infinite);

            """);
        project.Build();
        DockerPorts.EnsureRunsAlone();
        var pidFile = Path.Combine(project.ProjectDirectory, "stalling-runner.pid");

        var (exitCode, output) = project.Test(
            tests,
            new Dictionary<string, string>
            {
                ["INTEGRATION_TESTS_STARTUP_TIMEOUT"] = "00:01:30",
                ["STALLING_RUNNER_PID_FILE"] = pidFile,
            });

        exitCode.ShouldNotBe(0, output);
        File.Exists(pidFile).ShouldBeTrue($"The stalling MigrationRunner never started:{Environment.NewLine}{output}");
        var runner = int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture);
        try
        {
            IsRunning(runner).ShouldBeFalse($"The MigrationRunner (process {runner}) outlived the fixture's failed setup:{Environment.NewLine}{output}");
        }
        finally
        {
            if (IsRunning(runner))
            {
                using var survivor = Process.GetProcessById(runner);
                survivor.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>
    /// Whether a process runs. On Linux a killed one its parent has not reaped yet is a zombie, and runs no more; only
    /// <c>/proc</c> tells it apart, as the process API counts it running. Elsewhere the process API answers.
    /// </summary>
    private static bool IsRunning(int processId)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var stat = File.ReadAllText($"/proc/{processId}/stat");
                return stat[(stat.LastIndexOf(')') + 2)..][0] != 'Z';
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return false;
            }
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
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

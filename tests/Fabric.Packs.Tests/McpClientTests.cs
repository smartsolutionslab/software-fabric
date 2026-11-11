using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.mcp: [client]</c> (ADR-0010): the project uses external MCP servers. The AppHost declares each server, the
/// BuildingBlocks create the SDK's clients from that declaration, and code reaches the tools through one interface, or a
/// chat client through Microsoft.Extensions.AI's function invocation. An architecture rule keeps clients out of modules.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class McpClientTests
{
    private const string Profile = "lean-crud";
    private const string McpClient = "backend.mcp=client";

    private const string ClientInAModule = """
        using ModelContextProtocol.Client;

        namespace Shop.Modules.Billing.Infrastructure;

        public static class HandWiredDocs
        {
            public static Task<McpClient> ConnectAsync(CancellationToken cancellationToken) =>
                McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri("http://127.0.0.1:1/mcp") }), cancellationToken: cancellationToken);
        }
        """;

    [Fact]
    public void A_project_using_mcp_servers_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [McpClient]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_apphost_declares_the_servers_and_the_host_creates_the_clients_in_one_place()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [McpClient]);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        files["src/AppHost/Wiring/McpServers.cs"].ShouldContain("WithMcpServerUrl");
        files["src/AppHost/Wiring/McpServers.cs"].ShouldContain("WithMcpServerCommand");
        files["src/BuildingBlocks/Mcp/IMcpTools.cs"].ShouldContain("interface IMcpTools");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldContain("ModelContextProtocol.Core");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddMcpClients(builder.Configuration);");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/McpClientsAreCreatedOnlyByTheBuildingBlocks.cs");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/NoModelClients.cs");
        files["Solution.slnx"].ShouldContain("tests/Mcp.Tests/Mcp.Tests.csproj");
        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"ModelContextProtocol.Core\" Version=\"2.4.0\" />");
    }

    [Fact]
    public void The_generated_mcp_tests_reach_a_declared_server_and_the_architecture_rules_pass()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.Build();

        var (mcpExitCode, mcpOutput) = project.Test("tests/Mcp.Tests/Mcp.Tests.csproj");
        mcpExitCode.ShouldBe(0, mcpOutput);
        mcpOutput.ShouldContain("failed: 0");
        mcpOutput.ShouldContain("succeeded: 6");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_generated_mcp_tests_leave_out_the_servers_the_application_declares()
    {
        using var project = NewProject();
        var appHost = Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs");
        var declared = File.ReadAllText(appHost).Replace(
            "builder.Build().Run();",
            "host.WithMcpServerUrl(\"docs\", builder.AddParameter(\"mcp-docs-url\"), builder.AddParameter(\"mcp-docs-token\", secret: true));\n\nbuilder.Build().Run();",
            StringComparison.Ordinal);
        declared.ShouldContain("WithMcpServerUrl(\"docs\"");
        File.WriteAllText(appHost, declared);
        project.Build();

        var (exitCode, output) = project.Test("tests/Mcp.Tests/Mcp.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("succeeded: 6");
    }

    [Fact]
    public void An_mcp_client_created_in_a_module_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Infrastructure/HandWiredDocs.cs", ClientInAModule);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Infrastructure references ModelContextProtocol.Core, but MCP clients are created only by AddMcpClients");
    }

    [Fact]
    public void The_host_starts_with_no_server_declared_and_the_integration_tests_pass()
    {
        using var project = NewProject();
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    /// <summary>A lean-crud project using MCP servers; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject()
    {
        var project = new GeneratedProject(Profile, McpClient);
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

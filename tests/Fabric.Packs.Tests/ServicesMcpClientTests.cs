using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.mcp: [client]</c> with <c>backend.topology: services</c> (#759): the MCP clients run in the service hosts,
/// which call <c>AddMcpClients</c>; the edge, the AppHost's resource named <c>host</c>, runs no module code. So a
/// server is declared on the service hosts, in the AppHost's loop over them, the AppHost refuses one declared on the
/// edge, and <c>tests/Mcp.Tests</c> checks the configuration the service hosts receive. A modular monolith keeps
/// declaring its servers on <c>host</c>, its one host.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ServicesMcpClientTests
{
    private const string Profile = "saas-standard-react";
    private const string McpClient = "backend.mcp=client";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none", McpClient];

    private const string Loop = "foreach (var (module, project) in builder.ServiceHosts())";
    private const string EdgeRoutes = "    // The edge routes /<module>/...";

    /// <summary>The servers the test declares through DeclaredServers, and the resources it declared them on.</summary>
    private const string DeclaredHosts = """
        using Aspire.Hosting;

        namespace Shop.Mcp.Tests;

        public sealed class DeclaredHostsTests
        {
            [Fact]
            public async Task The_test_declares_its_servers_on_every_service_host_and_not_on_the_edge()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                List<string> hosts = [];

                var configuration = await DeclaredServers.HostConfigurationAsync(
                    (builder, host) =>
                    {
                        hosts.Add(host.Resource.Name);
                        host.WithMcpServerUrl("echo-server", builder.AddParameter("echo-server-url"));
                    },
                    new Dictionary<string, string> { ["echo-server-url"] = "http://127.0.0.1:1/mcp" },
                    cancellationToken);

                string.Join(",", hosts).ShouldBe("billing,shipping");
                configuration["Mcp:Servers:echo-server:Url"].ShouldBe("http://127.0.0.1:1/mcp");
            }
        }
        """;

    [Fact]
    public void With_services_the_servers_are_declared_on_the_service_hosts_in_the_apphosts_loop()
    {
        var files = Generate(Services);

        files["README.md"].ShouldContain("service.WithMcpServerUrl(\"docs\", docsUrl, docsToken)");
        files["README.md"].ShouldNotContain("host.WithMcpServerUrl");
        files["AGENTS.md"].ShouldContain("on `service` in the loop over `builder.ServiceHosts()`");
        files["AGENTS.md"].ShouldNotContain("Declare an external MCP server on the host");
        files["src/AppHost/Wiring/McpServers.cs"].ShouldContain("ServiceHosts()", customMessage: "the AppHost refuses a server declared on anything but a service host.");
        files["tests/Mcp.Tests/DeclaredServers.cs"].ShouldNotContain("resource.Name == \"host\"", customMessage: "under services, host is the edge.");
    }

    [Fact]
    public void A_modular_monolith_declares_its_servers_on_its_one_host()
    {
        var files = Generate(["backend.tenancy=none", McpClient]);

        files["README.md"].ShouldContain("host.WithMcpServerUrl(\"docs\"");
        files["AGENTS.md"].ShouldContain("Declare an external MCP server on the host");
        files["src/AppHost/Wiring/McpServers.cs"].ShouldNotContain("ServiceHosts");
        files["tests/Mcp.Tests/DeclaredServers.cs"].ShouldContain("resource.Name == \"host\"");
    }

    [Fact]
    public void With_services_the_mcp_tests_check_every_service_hosts_configuration_and_the_edge_is_refused()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("shipping");
        var appHost = Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs");
        var source = File.ReadAllText(appHost);
        source.ShouldContain(Loop);
        source.ShouldContain(EdgeRoutes);
        File.WriteAllText(
            appHost,
            source
                .Replace(Loop, "var docsUrl = builder.AddParameter(\"mcp-docs-url\");\nvar docsToken = builder.AddParameter(\"mcp-docs-token\", secret: true);\n\n" + Loop, StringComparison.Ordinal)
                .Replace(EdgeRoutes, "    service.WithMcpServerUrl(\"docs\", docsUrl, docsToken);\n\n" + EdgeRoutes, StringComparison.Ordinal));
        project.Write("tests/Mcp.Tests/DeclaredHostsTests.cs", DeclaredHosts);
        project.Build();

        var (exitCode, output) = project.Test("tests/Mcp.Tests/Mcp.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 8", customMessage: "the six client tests, the refused edge and the declared hosts.");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    private static Dictionary<string, string> Generate(IReadOnlyList<string> overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
    }
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.mcp: [server]</c> with <c>backend.topology: services</c> (ADR-0010 and ADR-0017, amended 2026-11-18,
/// #766): each service is its own MCP server, at <c>/{module}/mcp</c>, offering its own module's tools; the edge hosts
/// none and forwards <c>/{module}/**</c> as it does every module route. The MCP server tests are generated once per
/// service, against that service's host. A modular monolith keeps one server at <c>/mcp</c> and one
/// <c>tests/Mcp.Server.Tests</c> against <c>src/Host</c>.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class ServicesMcpServerTests
{
    private const string Profile = "saas-standard-react";
    private const string McpServer = "backend.mcp=server";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none", McpServer];

    private const string CountInvoices = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.CountInvoices;

        [McpTool("Counts the invoices.")]
        public sealed record CountInvoices : IQuery<Result<int, ApiError>>;

        public sealed class CountInvoicesHandler : IQueryHandler<CountInvoices, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountInvoices query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<int, ApiError>>(Success(0));
        }
        """;

    private const string CountParcels = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Shipping.Application.Queries.CountParcels;

        [McpTool("Counts the parcels.")]
        public sealed record CountParcels : IQuery<Result<int, ApiError>>;

        public sealed class CountParcelsHandler : IQueryHandler<CountParcels, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountParcels query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<int, ApiError>>(Success(0));
        }
        """;

    private const string FindInvoiceInBilling = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.FindInvoice;

        [McpTool("Finds an invoice.")]
        public sealed record FindInvoice : IQuery<Result<int, ApiError>>;

        public sealed class FindInvoiceHandler : IQueryHandler<FindInvoice, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(FindInvoice query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<int, ApiError>>(Success(0));
        }
        """;

    private const string ArchivedFindInvoiceInBilling = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Archive;

        [McpTool("Finds an archived invoice.")]
        public sealed record FindInvoice : IQuery<Result<int, ApiError>>;

        public sealed class FindArchivedInvoiceHandler : IQueryHandler<FindInvoice, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(FindInvoice query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<int, ApiError>>(Success(0));
        }
        """;

    private const string InvoiceInBillingFind = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.BillingFind.Application.Queries.Invoice;

        [McpTool("Finds an invoice, too.")]
        public sealed record Invoice : IQuery<Result<int, ApiError>>;

        public sealed class InvoiceHandler : IQueryHandler<Invoice, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(Invoice query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<int, ApiError>>(Success(0));
        }
        """;

    [Fact]
    public void With_services_each_service_host_maps_its_mcp_server_under_its_module_and_the_edge_maps_none()
    {
        var files = WithModules(Services, "billing", "order-items");

        files["src/Host/Wiring/McpServerEndpoint.cs"].ShouldContain("ServiceModule.Name");
        files["src/Host/Wiring/McpServerEndpoint.cs"].ShouldContain("/mcp");
        files["src/Host/Wiring/McpServerEndpoint.cs"].ShouldNotContain("billing", Case.Insensitive);
        foreach (var module in new[] { "Billing", "OrderItems" })
        {
            files[$"src/Services/{module}/Host/Program.cs"].ShouldContain("builder.Services.AddMcpServerTools(modules);");
            files[$"src/Services/{module}/Host/Program.cs"].ShouldContain("app.MapMcpServer();");
        }

        files["src/Edge/Program.cs"].ShouldNotContain("Mcp");
        files["tests/Architecture.Tests/Rules/SingletonsLiveOnTheEdge.cs"].ShouldContain("\"/mcp\"", customMessage: "/mcp at a service's root stays forbidden.");
    }

    [Fact]
    public void With_services_each_service_has_its_own_mcp_server_tests_against_its_own_host()
    {
        var files = WithModules(Services, "billing", "order-items");

        files.Keys.ShouldNotContain(path => path.StartsWith("tests/Mcp.Server.Tests/", StringComparison.Ordinal), "a services project has no MCP server tests against src/Host.");
        foreach (var module in new[] { "Billing", "OrderItems" })
        {
            files.ShouldNotContainKey($"tests/Services/{module}/Mcp.Server.Tests/Mcp.Server.Tests.csproj", "test.sh names a coverage report after its project's file: one name for every service keeps only the last report.");
            var project = files[$"tests/Services/{module}/Mcp.Server.Tests/{module}.Mcp.Server.Tests.csproj"];
            project.ShouldContain($"<ProjectReference Include=\"../../../../src/Services/{module}/Host/Host.csproj\" />");
            project.ShouldNotContain("src/Host/Host.csproj");
            files.Keys.ShouldContain($"tests/Services/{module}/Mcp.Server.Tests/McpHost.cs");
            files.Keys.ShouldContain($"tests/Services/{module}/Mcp.Server.Tests/McpServerTests.cs");
        }

        files["tests/Integration.Tests/McpEndpointTests.cs"].ShouldNotContain("new(\"/mcp\"", customMessage: "through the edge, a service's MCP server is at /<module>/mcp.");
    }

    [Fact]
    public void A_modular_monolith_keeps_one_mcp_server_at_the_root_and_one_test_project_against_src_Host()
    {
        var files = WithModules(["backend.tenancy=none", McpServer], "billing");

        files["src/Host/Wiring/McpServerEndpoint.cs"].ShouldContain("public const string Route = \"/mcp\";");
        files["src/Host/Wiring/McpServerEndpoint.cs"].ShouldNotContain("ServiceModule");
        files["tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj"].ShouldContain("<ProjectReference Include=\"../../src/Host/Host.csproj\" />");
        files.Keys.ShouldNotContain(path => path.StartsWith("tests/Services/", StringComparison.Ordinal));
        files["Solution.slnx"].ShouldContain("tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj");
    }

    [Fact]
    public void With_services_each_service_s_tool_names_are_checked_within_the_service_and_not_across_services()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("billing-find");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/FindInvoice", FindInvoiceInBilling);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Archive", ArchivedFindInvoiceInBilling);
        project.WriteTypes("src/Modules/BillingFind/BillingFind.Application/Queries/Invoice", InvoiceInBillingFind);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain(
            "Shop.Modules.Billing.Application.Queries.Archive.FindInvoice and Shop.Modules.Billing.Application.Queries.FindInvoice.FindInvoice would all be the MCP tool billing_find_invoice",
            customMessage: "two requests of one service still cannot share a tool name.");
        output.ShouldNotContain(
            "Shop.Modules.BillingFind.Application.Queries.Invoice.Invoice",
            customMessage: "billing-find is its own service, its own MCP server: its billing_find_invoice is no other service's.");
    }

    [Fact]
    public void Each_services_mcp_server_offers_its_own_modules_tools_its_coverage_report_reaches_the_gate_and_its_tests_pass()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountInvoices", CountInvoices);
        project.WriteTypes("src/Modules/Shipping/Shipping.Application/Queries/CountParcels", CountParcels);
        project.Write("tests/Services/Billing/Mcp.Server.Tests/ServiceToolsTests.cs", ServiceTools("billing_count_invoices", "shipping_count_parcels"));
        project.Write("tests/Services/Shipping/Mcp.Server.Tests/ServiceToolsTests.cs", ServiceTools("shipping_count_parcels", "billing_count_invoices"));
        project.Build();

        // Each service's MCP server tests load only their own module, so the gate sees a module's Application only if
        // that service's coverage report survived test.sh. Which file names the projects have is not the point.
        string[] serverTests =
        [
            .. Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "tests", "Services"), "*.Tests.csproj", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(project.ProjectDirectory, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
        serverTests.Length.ShouldBe(2);
        var (_, gateOutput) = Shell.Run(project.ProjectDirectory, "bash", [".github/scripts/test.sh", .. serverTests]);
        foreach (var module in new[] { "Billing", "Shipping" })
        {
            gateOutput.ShouldMatch($@"Shop\.Modules\.{module}\.Application: \d+(\.\d)? % line coverage", $"the {module} service's coverage report did not reach the gate:\n{gateOutput}");
        }

        foreach (var module in new[] { "Billing", "Shipping" })
        {
            var (exitCode, output) = project.Test($"tests/Services/{module}/Mcp.Server.Tests/{module}.Mcp.Server.Tests.csproj");
            exitCode.ShouldBe(0, output);
            output.ShouldContain("failed: 0");
            Succeeded().IsMatch(output).ShouldBeTrue(output);
        }

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    /// <summary>A test of one service's MCP server: it lists that service's module's tool, and not the other module's.</summary>
    private static string ServiceTools(string own, string other) => $$"""
        namespace Shop.Mcp.Server.Tests;

        public sealed class ServiceToolsTests : IDisposable
        {
            private readonly McpHost host = new();

            [Fact]
            public async Task The_service_offers_its_own_modules_tool_and_not_another_modules()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var client = await host.ConnectAsync("developer", [], cancellationToken);

                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

                tools.Select(tool => tool.Name).ShouldContain("{{own}}");
                tools.Select(tool => tool.Name).ShouldNotContain("{{other}}");
            }

            public void Dispose() => host.Dispose();
        }
        """;

    private static Dictionary<string, string> WithModules(IReadOnlyList<string> overrides, params string[] modules)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
            var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, module, []);
            addition.Errors.ShouldBeEmpty();
            foreach (var file in addition.Created)
            {
                files.Add(file.Path, file.Content);
            }

            files[FabricFile.FileName] = addition.FabricYaml!;
        }

        return files;
    }

    [GeneratedRegex(@"succeeded: [1-9]")]
    private static partial Regex Succeeded();
}

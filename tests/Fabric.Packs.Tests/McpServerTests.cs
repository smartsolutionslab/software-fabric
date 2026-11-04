using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.mcp: [server]</c> (ADR-0010): the project exposes MCP tools. A command or query becomes a tool only when
/// marked <c>[McpTool("description")]</c>; the tool's arguments are the request record, and a call goes through the
/// dispatcher as an endpoint's does. The endpoint needs a signed-in caller unless <c>backend.auth: none</c>, and a tool
/// that names a policy enforces it. One definition of a valid tool serves the host's discovery and an architecture rule.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class McpServerTests
{
    private const string Profile = "lean-crud";
    private const string McpServer = "backend.mcp=server";
    private const string Roles = "backend.authorization=roles";
    private const string Sdk = "<PackageVersion Include=\"ModelContextProtocol.AspNetCore\" Version=\"2.3.0\" />";

    private const string MarkedQuery = """
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

    private const string MarkedWithoutDescription = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.CountInvoices;

        [McpTool(" ")]
        public sealed record CountInvoices : IQuery<Result<int, ApiError>>;

        public sealed class CountInvoicesHandler : IQueryHandler<CountInvoices, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountInvoices query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<int, ApiError>>(Success(0));
        }
        """;

    private const string MarkedWithoutHandler = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.CountInvoices;

        [McpTool("Counts the invoices.")]
        public sealed record CountInvoices : IQuery<Result<int, ApiError>>;
        """;

    private const string MarkedWithItsModulesPolicy = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.CountInvoices;

        [McpTool("Counts the invoices.", Policy = BillingPolicyNames.Manage)]
        public sealed record CountInvoices : IQuery<Result<int, ApiError>>;

        public sealed class CountInvoicesHandler : IQueryHandler<CountInvoices, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountInvoices query, CancellationToken cancellationToken) =>
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

    private const string MarkedWithAnotherModulesPolicy = """
        using Shop.BuildingBlocks.Mcp;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.CountInvoices;

        [McpTool("Counts the invoices.", Policy = "shipping.manage")]
        public sealed record CountInvoices : IQuery<Result<int, ApiError>>;

        public sealed class CountInvoicesHandler : IQueryHandler<CountInvoices, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountInvoices query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<int, ApiError>>(Success(0));
        }
        """;

    [Fact]
    public void A_project_exposing_mcp_tools_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [McpServer]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_host_maps_the_endpoint_for_signed_in_callers_and_offers_the_marked_requests()
    {
        var files = Files(Profile, McpServer);

        files["src/Host/Host.csproj"].ShouldContain("<PackageReference Include=\"ModelContextProtocol.AspNetCore\" />");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldNotContain("ModelContextProtocol");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddMcpServerTools(modules);");
        files["src/Host/Program.cs"].ShouldContain("app.MapMcpServer();");
        files["src/Host/Wiring/McpServerEndpoint.cs"].ShouldContain(".RequireAuthorization()");
        files["src/BuildingBlocks/Mcp/McpToolAttribute.cs"].ShouldContain("public sealed class McpToolAttribute");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/EveryMcpToolIsValid.cs");
        files["Solution.slnx"].ShouldContain("tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj");
    }

    [Fact]
    public void Without_authentication_the_endpoint_is_open_and_a_tool_names_no_policy()
    {
        var files = Files(Profile, McpServer, "backend.auth=none");

        files["src/Host/Wiring/McpServerEndpoint.cs"].ShouldNotContain("RequireAuthorization");
        files["src/BuildingBlocks/Mcp/McpToolAttribute.cs"].ShouldNotContain("Policy");
    }

    [Fact]
    public void The_base_declares_the_sdk_once_whether_the_project_exposes_uses_or_both()
    {
        Count(Files(Profile, McpServer)["Directory.Packages.props"], Sdk).ShouldBe(1);
        Count(Files(Profile, "backend.mcp=client")["Directory.Packages.props"], Sdk).ShouldBe(1);
        Count(Files(Profile, "backend.mcp=server,client")["Directory.Packages.props"], Sdk).ShouldBe(1);
        foreach (var profile in new[] { "lean-crud", "saas-standard-react", "saas-strict-react" })
        {
            Files(profile)["Directory.Packages.props"].ShouldNotContain("ModelContextProtocol", customMessage: profile);
        }
    }

    [Fact]
    public void The_generated_mcp_server_tests_pass_and_a_tool_naming_its_modules_policy_passes_the_architecture_rules()
    {
        using var project = NewProject(McpServer, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountInvoices", MarkedWithItsModulesPolicy);
        project.Build();

        var (mcpExitCode, mcpOutput) = project.Test("tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj");
        mcpExitCode.ShouldBe(0, mcpOutput);
        mcpOutput.ShouldContain("failed: 0");
        mcpOutput.ShouldContain("succeeded: 13");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void With_nullable_absence_and_minimal_results_the_generated_mcp_server_tests_pass()
    {
        // The BCL's guards: minimal's own Result does not compile with Ensure.That's (on main too), which is not this pack's.
        using var project = NewProject(McpServer, Roles, "backend.absence=nullable", "backend.result-style=minimal", "backend.guards=bcl");
        project.Build();

        var (exitCode, output) = project.Test("tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 10");
    }

    [Fact]
    public void Without_authentication_the_generated_mcp_server_tests_pass()
    {
        using var project = NewProject(McpServer, "backend.auth=none");
        project.Build();

        var (exitCode, output) = project.Test("tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 11");
    }

    [Fact]
    public void A_project_both_exposing_and_using_mcp_builds_and_both_test_projects_pass()
    {
        using var project = NewProject("backend.mcp=server,client");
        project.Build();

        var (serverExitCode, serverOutput) = project.Test("tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj");
        serverExitCode.ShouldBe(0, serverOutput);
        serverOutput.ShouldContain("failed: 0");

        var (clientExitCode, clientOutput) = project.Test("tests/Mcp.Tests/Mcp.Tests.csproj");
        clientExitCode.ShouldBe(0, clientOutput);
        clientOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_marked_query_without_a_description_fails_the_architecture_tests_naming_it()
    {
        var output = ArchitectureFailure(MarkedWithoutDescription);

        output.ShouldContain("Shop.Modules.Billing.Application.Queries.CountInvoices.CountInvoices is marked [McpTool] without a description");
    }

    [Fact]
    public void A_marked_query_without_a_handler_fails_the_architecture_tests_naming_it()
    {
        var output = ArchitectureFailure(MarkedWithoutHandler);

        output.ShouldContain("Shop.Modules.Billing.Application.Queries.CountInvoices.CountInvoices is marked [McpTool] but has 0 handlers");
    }

    [Fact]
    public void A_marked_query_naming_a_policy_its_module_does_not_register_fails_the_architecture_tests_naming_it()
    {
        var output = ArchitectureFailure(MarkedWithAnotherModulesPolicy);

        output.ShouldContain("Shop.Modules.Billing.Application.Queries.CountInvoices.CountInvoices is marked [McpTool] with the policy shipping.manage, which the module Billing does not register");
    }

    [Fact]
    public void The_host_starts_with_a_module_marking_a_query_and_the_integration_tests_pass()
    {
        // Without roles the module adds no endpoint, so the frontend's OpenAPI copy stays current; /mcp is not in it.
        using var project = NewProject(McpServer);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountInvoices", MarkedQuery);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public async Task Tool_names_that_collide_across_modules_fail_the_architecture_tests_and_stop_the_host_naming_both()
    {
        using var project = NewProject(McpServer);
        project.AddModule("billing");
        project.AddModule("billing-find");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/FindInvoice", FindInvoiceInBilling);
        project.WriteTypes("src/Modules/BillingFind/BillingFind.Application/Queries/Invoice", InvoiceInBillingFind);
        project.Build();
        const string Collision = "Shop.Modules.Billing.Application.Queries.FindInvoice.FindInvoice and Shop.Modules.BillingFind.Application.Queries.Invoice.Invoice would all be the MCP tool billing_find_invoice";

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain(Collision);

        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = "Host=127.0.0.1;Port=1;Database=unused",
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/unused",
        });
        var stopped = await Should.ThrowAsync<InvalidOperationException>(() => host.GetAsync("/alive"));
        stopped.Message.ShouldContain(Collision);
    }

    /// <summary>The architecture tests' output for a lean-crud project with roles whose billing module holds the source.</summary>
    private static string ArchitectureFailure(string source)
    {
        using var project = NewProject(McpServer, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountInvoices", source);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        return output;
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static int Count(string text, string line) =>
        text.Split('\n').Count(candidate => candidate.Trim() == line);

    /// <summary>A lean-crud project with the overrides; fails while any of its options is generated by a stand-in.</summary>
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

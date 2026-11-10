namespace Fabric.Cli.Tests;

public class ValidateCommandTests
{
    [Fact]
    public void A_valid_profile_exits_zero()
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-react", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(0);
        run.Output.ShouldStartWith("valid: profile saas-standard-react");
        run.Error.ShouldBeEmpty();
    }

    [Fact]
    public void Uses_the_catalog_shipped_next_to_the_tool_by_default()
    {
        CliRun.Of("validate", "--profile", "lean-crud").ExitCode.ShouldBe(0);
    }

    [Fact]
    public void A_rule_violation_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-vue", "--set", "frontend.state=redux-toolkit", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: frontend.state=redux-toolkit requires frontend.framework == react, but frontend.framework is vue.");
    }

    [Fact]
    public void Marten_with_a_tenancy_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "industrial-event-driven-mfe", "--set", "backend.tenancy=column", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: module defaults: module.event-store=marten requires backend.tenancy == none, but backend.tenancy is column.");
    }

    [Fact]
    public void The_wolverine_mediator_without_the_wolverine_messaging_library_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "lean-crud", "--set", "backend.mediator=wolverine", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: backend.mediator=wolverine requires backend.bus.library == wolverine, but backend.bus.library is not set.");
    }

    [Fact]
    public void The_wolverine_mediator_with_the_wolverine_messaging_library_is_valid()
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-react", "--set", "backend.mediator=wolverine", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(0, run.Error);
    }

    /// <summary>Their generated tests start Wolverine in process (#362).</summary>
    [Fact]
    public void The_wolverine_mediator_with_agents_dashboards_grpc_and_an_mcp_server_is_valid()
    {
        var run = CliRun.Of(
            "validate",
            "--profile",
            "saas-ai-react",
            "--set",
            "backend.mediator=wolverine",
            "--set",
            "backend.api-protocols=rest,grpc",
            "--set",
            "backend.mcp=server",
            "--catalog",
            Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(0, run.Error);
    }

    [Fact]
    public void Contract_interfaces_without_integration_events_exit_one_and_print_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "lean-crud", "--set", "module.communication=contract-interface", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: module defaults: module.communication requires module.communication has integration-events, but module.communication is contract-interface.");
    }

    [Fact]
    public void An_empty_communication_set_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "lean-crud", "--set", "module.communication=", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: module defaults: module.communication requires module.communication has integration-events, but module.communication is empty.");
    }

    /// <summary>Every API client is generated from the OpenAPI document, which only the rest protocol serves (#415).</summary>
    [Fact]
    public void An_api_client_without_the_rest_protocol_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "lean-crud", "--set", "backend.api-protocols=", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: frontend.api-client=orval requires backend.api-protocols has rest, but backend.api-protocols is empty.");
    }

    /// <summary>Without a signed-in user there is no trustworthy tenant (#464).</summary>
    [Fact]
    public void Tenancy_without_authentication_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-react", "--set", "backend.auth=none", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: backend.tenancy=column requires backend.auth != none, but backend.auth is none.");
    }

    /// <summary>The agent panel is built for a single-page app only (#518).</summary>
    [Fact]
    public void An_agent_panel_in_micro_frontends_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "saas-ai-react", "--set", "frontend.architecture=micro-frontends", "--set", "frontend.design-system=tokens", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: frontend.agent-ui=ag-ui requires frontend.architecture == spa, but frontend.architecture is micro-frontends.");
    }

    /// <summary>Orval generates TanStack Query hooks, or Pinia's plain functions; Redux Toolkit's server data is RTK Query (#518).</summary>
    [Fact]
    public void Orval_with_redux_toolkit_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-react", "--set", "frontend.api-client=orval", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: frontend.api-client=orval requires frontend.state in (zustand-tanstack-query, tanstack-query, pinia-tanstack-query, pinia), but frontend.state is redux-toolkit.");
    }

    [Fact]
    public void Several_overrides_are_applied()
    {
        var run = CliRun.Of(
            "validate",
            "--profile", "lean-crud",
            "--set", "backend.bus.transport=kafka",
            "--set", "backend.topology=services",
            "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void An_unknown_dimension_exits_one()
    {
        var run = CliRun.Of("validate", "--set", "frontend.colour=red", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: unknown dimension 'frontend.colour'.");
    }

    [Theory]
    [InlineData("backend.bus.library=masstransit", "error: 'masstransit' is not an option of backend.bus.library; options: wolverine.")]
    [InlineData("backend.test.extras=contract-tests", "error: 'contract-tests' is not an option of backend.test.extras; options: mutation-testing.")]
    public void A_dropped_option_exits_one_as_an_unknown_option(string setting, string error)
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-react", "--set", setting, "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain(error);
    }

    [Fact]
    public void A_broken_catalog_exits_one_with_its_location()
    {
        var catalog = Path.Combine(Directory.CreateTempSubdirectory("fabric-cli-").FullName, "catalog.yaml");
        File.WriteAllText(catalog, "version: 1\ndimensions:\n  a.b: { scope: everywhere, options: { x: { tier: supported } } }\n");

        var run = CliRun.Of("validate", "--catalog", catalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain($"error: {catalog}:3:");
    }
}

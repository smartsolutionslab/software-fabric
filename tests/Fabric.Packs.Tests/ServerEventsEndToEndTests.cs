namespace Fabric.Packs.Tests;

/// <summary>The running application: a server event reaches a signed-in browser, and only the browsers of its tenant.</summary>
[Collection(FixedPorts.Name)]
public sealed class ServerEventsEndToEndTests
{
    [Fact]
    public void A_server_event_reaches_the_browser_in_the_integration_tests_and_end_to_end_and_only_its_tenant()
    {
        using var project = new GeneratedProject("saas-standard-react", "frontend.realtime=signalr");
        project.AddModule("billing");
        project.Build();

        var (listExitCode, listed) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--list-tests");
        listExitCode.ShouldBe(0, listed);
        listed.ShouldContain("Shop.Integration.Tests.ServerEventsTests.A_test_event_reaches_the_browsers_of_the_senders_tenant_only");
        listed.ShouldContain("Shop.Integration.Tests.ServerEventsTests.An_anonymous_connection_is_refused");
        listed.ShouldContain("Shop.Integration.Tests.ServerEventsTests.A_connection_is_closed_once_its_access_token_expires");

        var (integrationExitCode, integration) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");
        integrationExitCode.ShouldBe(0, integration);
        integration.ShouldContain("failed: 0");

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        // The generated CI's own end-to-end step: start the AppHost, wait for the app, run Playwright, stop.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("2 passed");

        // The gate: an event sent to every browser instead of its tenant's fails the generated integration tests.
        var sender = Path.Combine(project.ProjectDirectory, "src", "Host", "Realtime", "HubBrowserEvents.cs");
        var tenantOnly = File.ReadAllText(sender);
        tenantOnly.ShouldContain("hub.Clients.Group(TenantGroup(tenantId))");
        File.WriteAllText(sender, tenantOnly.Replace("hub.Clients.Group(TenantGroup(tenantId))", "hub.Clients.All", StringComparison.Ordinal));
        project.Build();

        var (brokenExitCode, broken) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");
        brokenExitCode.ShouldNotBe(0, broken);
        broken.ShouldContain("failed Shop.Integration.Tests.ServerEventsTests.A_test_event_reaches_the_browsers_of_the_senders_tenant_only");
    }

    [Fact]
    public void Under_schema_per_tenant_a_server_event_reaches_the_browsers_of_its_tenant_only()
    {
        using var project = new GeneratedProject("saas-standard-react", "frontend.realtime=signalr", "backend.tenancy=schema-per-tenant");
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-method", "Shop.Integration.Tests.ServerEventsTests.A_test_event_reaches_the_browsers_of_the_senders_tenant_only");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("succeeded: 1", customMessage: output);
        output.ShouldContain("failed: 0", customMessage: output);
    }
}

using System.Net;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.auth: none</c> (ADR-0009): no identity provider, no token validation, no sign-in. Every endpoint is
/// anonymous by choice, and an architecture rule keeps it that way until the project chooses an identity provider.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class NoAuthenticationTests
{
    private const string Profile = "lean-crud";
    private const string NoAuthentication = "backend.auth=none";
    private const string DatabasePassword = "fabric-tests";

    private const string ProbeEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api;

        public sealed class Probe : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/probe", () => TypedResults.Ok("pong"));
        }
        """;

    [Fact]
    public void A_project_without_authentication_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [NoAuthentication]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_project_has_no_identity_provider_no_me_endpoint_and_no_sign_in()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [NoAuthentication]);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        files.Keys.ShouldNotContain("src/AppHost/realms/realm.json");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("Keycloak");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("Authentication__Authority");
        files["src/Host/Program.cs"].ShouldNotContain("Authentication");
        files["src/Host/Program.cs"].ShouldNotContain("UseAuthorization");
        files["src/Host/Program.cs"].ShouldNotContain("\"/me\"");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/auth/", StringComparison.Ordinal));
        files["web/app/package.json"].ShouldNotContain("oidc");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/NoAuthentication.cs");
    }

    [Fact]
    public void A_project_without_authentication_builds_and_a_new_modules_tests_and_the_architecture_rules_pass()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public async Task A_module_endpoint_answers_200_without_a_token()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/Probe.cs", ProbeEndpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        });

        (await host.GetAsync("/billing/probe")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.GetAsync("/me")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void An_endpoint_that_requires_authorization_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/BillingModule.cs", BillingModule(
            services: "services.AddModuleInfrastructure<BillingModule>(configuration);",
            extraMapping: "endpoints.MapGet(\"/billing-closed\", () => \"closed\").RequireAuthorization();"));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("GET /billing-closed requires authorization, but the project has no authentication (backend.auth: none).");
    }

    [Fact]
    public void An_authentication_scheme_added_by_hand_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/BillingModule.cs", BillingModule(
            services: "services.AddModuleInfrastructure<BillingModule>(configuration).AddAuthentication().AddCookie();",
            extraMapping: "endpoints.MapGet(\"/billing-open\", () => \"open\");"));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("The host registers the authentication scheme Cookies, but the project has no authentication (backend.auth: none).");
    }

    [Fact]
    public void The_web_app_without_an_auth_client_installs_builds_lints_and_passes_its_tests()
    {
        using var project = NewProject();
        project.AddModule("billing");

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }
    }

    [Fact]
    public void The_generated_end_to_end_step_passes_with_an_anonymous_visitor_seeing_the_application()
    {
        using var project = NewProject();
        project.AddModule("billing");
        project.Build();

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        // The generated CI's own end-to-end step: start the AppHost, wait for the app, run Playwright, stop.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("1 passed");
    }

    [Fact]
    public void Column_tenancy_without_authentication_fails_the_build_saying_why()
    {
        using var project = new GeneratedProject("saas-standard-react", NoAuthentication);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("The column tenancy pack reads the tenant from the signed-in user's token");
    }

    /// <summary>A lean-crud project without authentication; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject()
    {
        var project = new GeneratedProject(Profile, NoAuthentication);
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

    private static string BillingModule(string services, string extraMapping) => $$"""
        using Shop.BuildingBlocks.Modules;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Modules.Billing.Api;

        public sealed class BillingModule : IModule
        {
            public void AddServices(IServiceCollection services, IConfiguration configuration) =>
                {{services}}

            public void MapEndpoints(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGroup("/billing").MapModuleEndpoints<BillingModule>();
                {{extraMapping}}
            }
        }
        """;
}

using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.authorization: [roles]</c> (ADR-0009): access by the identity provider's roles in the token. Roles need
/// someone to sign in, so the dimension exists only while the project authenticates. A module registers its own
/// policies, named <c>&lt;module&gt;.&lt;permission&gt;</c>; an endpoint names one, and only the policy registration
/// checks roles.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class RoleAuthorizationTests
{
    private const string Profile = "lean-crud";
    private const string Roles = "backend.authorization=roles";

    [Fact]
    public void A_project_with_roles_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Roles]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Roles_without_authentication_are_refused_saying_why()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, ["backend.auth=none", Roles]);

        generated.Errors.ShouldContain("backend.authorization is set to roles, but it only applies when backend.auth != none, and backend.auth is none.");
    }

    [Fact]
    public void A_project_without_authorization_choices_has_no_module_policies()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, []);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        files.Keys.ShouldNotContain("src/BuildingBlocks/Modules/IModulePolicies.cs");
        files.Keys.ShouldNotContain("tests/Architecture.Tests/Rules/EveryEndpointPolicyIsRegisteredByItsModule.cs");
        files["src/BuildingBlocks/Modules/ModuleInfrastructure.cs"].ShouldNotContain("AddModulePolicies");
        files["src/AppHost/realms/realm.json"].ShouldNotContain("manager");
    }

    [Fact]
    public void The_realm_has_a_manager_role_the_developer_holds_and_a_visitor_without_roles()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Roles]);
        generated.Errors.ShouldBeEmpty();
        var realm = System.Text.Json.Nodes.JsonNode.Parse(generated.Files.Single(file => file.Path == "src/AppHost/realms/realm.json").Content)!;

        realm["roles"]!["realm"]!.AsArray().Select(role => (string?)role!["name"]).ShouldBe(["manager"]);
        var users = realm["users"]!.AsArray().ToDictionary(user => (string)user!["username"]!, user => user!["realmRoles"]?.AsArray().Select(role => (string?)role).ToList() ?? []);
        users["developer"].ShouldBe(["manager"]);
        users["visitor"].ShouldBeEmpty();
    }

    [Fact]
    public void With_tenancy_both_developers_hold_the_manager_role_and_the_visitor_has_a_tenant()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "saas-standard-react", [Roles]);
        generated.Errors.ShouldBeEmpty();
        var realm = System.Text.Json.Nodes.JsonNode.Parse(generated.Files.Single(file => file.Path == "src/AppHost/realms/realm.json").Content)!;

        var users = realm["users"]!.AsArray().ToDictionary(user => (string)user!["username"]!);
        users["developer"]!["realmRoles"]!.AsArray().Select(role => (string?)role).ShouldBe(["manager"]);
        users["developer-b"]!["realmRoles"]!.AsArray().Select(role => (string?)role).ShouldBe(["manager"]);
        users["visitor"]!["realmRoles"].ShouldBeNull();
        users["visitor"]!["groups"]!.AsArray().Select(group => (string?)group).ShouldBe(["/tenants/tenant-a"]);
    }

    [Fact]
    public void A_new_module_registers_a_sample_policy_that_requires_the_manager_role()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");

        var policies = File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "Modules", "Billing", "Billing.Api", "BillingPolicies.cs"));
        policies.ShouldContain("public const string Manage = \"billing.manage\";");
        policies.ShouldContain("RequireRole(\"manager\")");
    }

    [Fact]
    public void The_untouched_project_with_a_module_builds_and_its_module_and_architecture_tests_pass()
    {
        using var project = NewProject(Profile);
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
    public void A_project_with_tenancy_and_roles_builds_and_its_architecture_tests_pass()
    {
        using var project = NewProject("saas-standard-react");
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void An_endpoint_naming_a_policy_its_module_does_not_register_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        project.AddModule("orders");
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", Endpoint("Billing", "Unregistered", "/unregistered", "\"billing.approve\""));
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", Endpoint("Billing", "Foreign", "/foreign", "\"orders.manage\""));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("GET /billing/unregistered requires the policy billing.approve, which the module Billing does not register.");
        output.ShouldContain("GET /billing/foreign requires the policy orders.manage, which the module Billing does not register.");
    }

    [Fact]
    public void Code_that_checks_roles_outside_the_policy_registration_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", """
            using System.Security.Claims;
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;

            namespace Shop.Modules.Billing.Api.Endpoints;

            public sealed class RoleChecks : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapGet("/in-role", (ClaimsPrincipal user) => TypedResults.Ok(user.IsInRole("manager")));
                    endpoints.MapGet("/role-claim", (ClaimsPrincipal user) => TypedResults.Ok(user.HasClaim(ClaimTypes.Role, "manager")));
                    endpoints.MapGet("/require-role", () => TypedResults.Ok()).RequireAuthorization(policy => policy.RequireRole("manager"));
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Modules/Billing/Billing.Api/Endpoints/RoleChecks.cs:14: checks a role (IsInRole)");
        output.ShouldContain("src/Modules/Billing/Billing.Api/Endpoints/RoleChecks.cs:15: checks a role (ClaimTypes.Role)");
        output.ShouldContain("src/Modules/Billing/Billing.Api/Endpoints/RoleChecks.cs:16: checks a role (RequireRole)");
    }

    [Fact]
    public void Roles_with_an_identity_provider_the_pack_does_not_handle_fail_the_build_saying_why()
    {
        using var project = new GeneratedProject(Profile, "backend.auth=entra-id", Roles);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("The roles pack reads Keycloak's realm and client roles");
    }

    [Fact]
    public void The_generated_integration_tests_give_a_manager_200_a_visitor_403_and_an_anonymous_caller_401()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests", "Integration.Tests", "BillingRoleTests.cs"))
            .ShouldContain("HttpStatusCode.Forbidden");
        project.Build();

        // The new module's sample endpoint makes the frontend's OpenAPI copy stale; that test fails and rewrites it.
        var (_, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        output.ShouldContain("failed: 1", customMessage: output);
        output.ShouldContain("failed Shop.Integration.Tests.OpenApiDocumentTests.The_frontends_copy_of_the_openapi_document_is_current", customMessage: output);
    }

    /// <summary>A project of the profile with roles; fails while the roles pack is a stand-in.</summary>
    private static GeneratedProject NewProject(string profile)
    {
        var project = new GeneratedProject(profile, Roles);
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

    private static string Endpoint(string module, string name, string route, string policy) => $$"""
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.{{module}}.Api.Endpoints;

        public sealed class {{name}} : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("{{route}}", () => TypedResults.Ok()).RequireAuthorization({{policy}});
        }
        """;
}

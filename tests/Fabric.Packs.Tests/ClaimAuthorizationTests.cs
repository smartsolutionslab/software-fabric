using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.authorization: [claims]</c> (ADR-0009): access by claim, profile information the identity provider puts in
/// the token. Alone or together with roles, where one policy may require a role and a claim. The sample claim is
/// <c>department</c>, a Keycloak user attribute; the module's sample policy <c>&lt;module&gt;.manage</c> requires
/// <c>department=finance</c>, and with roles the role <c>manager</c> too (owner decision on #74).
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ClaimAuthorizationTests
{
    private const string Profile = "lean-crud";
    private const string Claims = "backend.authorization=claims";
    private const string RolesAndClaims = "backend.authorization=roles,claims";

    [Fact]
    public void A_project_with_claims_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Claims]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_project_with_roles_and_claims_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [RolesAndClaims]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Claims_without_authentication_are_refused_saying_why()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, ["backend.auth=none", Claims]);

        generated.Errors.ShouldContain("backend.authorization is set to claims, but it only applies when backend.auth != none, and backend.auth is none.");
    }

    [Fact]
    public void The_realm_puts_the_department_attribute_into_the_access_token_and_only_an_administrator_may_edit_it()
    {
        var realm = Realm(Profile, Claims);

        var web = realm["clients"]!.AsArray().Single(client => (string?)client!["clientId"] == "shop-web")!;
        var mapper = web["protocolMappers"]!.AsArray().Single(mapper => (string?)mapper!["name"] == "department")!;
        ((string?)mapper["protocolMapper"]).ShouldBe("oidc-usermodel-attribute-mapper");
        ((string?)mapper["config"]!["user.attribute"]).ShouldBe("department");
        ((string?)mapper["config"]!["claim.name"]).ShouldBe("department");
        ((string?)mapper["config"]!["access.token.claim"]).ShouldBe("true");

        var profile = realm["components"]!["org.keycloak.userprofile.UserProfileProvider"]!.AsArray().Single()!;
        var config = JsonNode.Parse((string)profile["config"]!["kc.user.profile.config"]!.AsArray().Single()!)!;
        var attributes = config["attributes"]!.AsArray().ToDictionary(attribute => (string)attribute!["name"]!);
        attributes.Keys.ShouldBe(["username", "email", "firstName", "lastName", "department"]);
        attributes["department"]!["permissions"]!["edit"]!.AsArray().Select(role => (string?)role).ShouldBe(["admin"]);
    }

    [Fact]
    public void With_claims_the_developer_and_the_analyst_are_in_finance_the_clerk_in_sales_and_the_visitor_in_no_department()
    {
        var users = Users(Realm(Profile, Claims));

        Department(users["developer"]).ShouldBe("finance");
        Department(users["analyst"]).ShouldBe("finance");
        Department(users["clerk"]).ShouldBe("sales");
        Department(users["visitor"]).ShouldBeNull();
        users.Values.ShouldAllBe(user => user["realmRoles"] == null);
    }

    [Fact]
    public void With_roles_and_claims_the_clerk_has_only_the_role_and_the_analyst_only_the_claim()
    {
        var realm = Realm(Profile, RolesAndClaims);
        var users = Users(realm);

        realm["roles"]!["realm"]!.AsArray().Select(role => (string?)role!["name"]).ShouldBe(["manager"]);
        Roles(users["developer"]).ShouldBe(["manager"]);
        Department(users["developer"]).ShouldBe("finance");
        Roles(users["clerk"]).ShouldBe(["manager"]);
        Department(users["clerk"]).ShouldBe("sales");
        Roles(users["analyst"]).ShouldBeEmpty();
        Department(users["analyst"]).ShouldBe("finance");
        Roles(users["visitor"]).ShouldBeEmpty();
        Department(users["visitor"]).ShouldBeNull();
    }

    [Fact]
    public void With_tenancy_and_claims_the_second_developer_is_in_finance_and_the_new_users_have_a_tenant()
    {
        var users = Users(Realm("saas-standard-react", Claims));

        Department(users["developer-b"]).ShouldBe("finance");
        users["clerk"]["groups"]!.AsArray().Select(group => (string?)group).ShouldBe(["/tenants/tenant-a"]);
        users["analyst"]["groups"]!.AsArray().Select(group => (string?)group).ShouldBe(["/tenants/tenant-a"]);
    }

    [Fact]
    public void A_project_with_roles_alone_has_no_department_and_no_claim_users()
    {
        var realm = Realm(Profile, "backend.authorization=roles");

        realm.ToJsonString().ShouldNotContain("department");
        Users(realm).Keys.ShouldBe(["developer", "visitor"]);
    }

    [Fact]
    public void A_new_module_with_claims_registers_a_sample_policy_that_requires_the_finance_department()
    {
        using var project = NewProject(Profile, Claims);
        project.AddModule("billing");

        var policies = Policies(project);
        PolicyNames(project).ShouldContain("public const string Manage = \"billing.manage\";");
        policies.ShouldContain("RequireClaim(\"department\", \"finance\")");
        policies.ShouldNotContain("RequireRole");
    }

    [Fact]
    public void A_new_module_with_roles_and_claims_registers_a_sample_policy_that_requires_the_manager_role_and_the_finance_department()
    {
        using var project = NewProject(Profile, RolesAndClaims);
        project.AddModule("billing");

        var policies = Policies(project);
        policies.ShouldContain("RequireRole(\"manager\")");
        policies.ShouldContain("RequireClaim(\"department\", \"finance\")");
    }

    [Theory]
    [InlineData(Claims)]
    [InlineData(RolesAndClaims)]
    public void The_untouched_project_with_a_module_builds_and_its_module_and_architecture_tests_pass(string authorization)
    {
        using var project = NewProject(Profile, authorization);
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
    public void Code_that_checks_claims_outside_the_policy_registration_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Profile, Claims);
        project.AddModule("billing");
        project.AddModule("orders");
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", """
            using System.Security.Claims;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;

            namespace Shop.Modules.Billing.Api.Endpoints;

            public sealed class ClaimChecks : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapGet("/has-claim", (ClaimsPrincipal user) => TypedResults.Ok(user.HasClaim("department", "finance")));
                    endpoints.MapGet("/require-claim", () => TypedResults.Ok()).RequireAuthorization(policy => policy.RequireClaim("department", "finance"));
                    endpoints.MapGet("/department", (ClaimsPrincipal user) => TypedResults.Ok(user.FindFirst("department")?.Value));
                    endpoints.MapGet("/unregistered", () => TypedResults.Ok()).RequireAuthorization("billing.approve");
                    endpoints.MapGet("/foreign", () => TypedResults.Ok()).RequireAuthorization("orders.manage");
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Modules/Billing/Billing.Api/Endpoints/ClaimChecks.cs:13: checks a claim (HasClaim)");
        output.ShouldContain("src/Modules/Billing/Billing.Api/Endpoints/ClaimChecks.cs:14: checks a claim (RequireClaim)");
        output.ShouldNotContain("ClaimChecks.cs:15");
        output.ShouldContain("GET /billing/unregistered requires the policy billing.approve, which the module Billing does not register.");
        output.ShouldContain("GET /billing/foreign requires the policy orders.manage, which the module Billing does not register.");
    }

    [Theory]
    [InlineData(Claims, "A_user_in_another_department_is_forbidden")]
    [InlineData(RolesAndClaims, "A_user_with_only_the_manager_role_is_forbidden")]
    public void The_generated_integration_tests_prove_the_sample_policy_against_the_running_stack(string authorization, string generatedTest)
    {
        using var project = NewProject(Profile, authorization);
        project.AddModule("billing");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests", "Integration.Tests", "BillingClaimTests.cs"))
            .ShouldContain(generatedTest);
        project.Build();

        // The new module's sample endpoint is not in the OpenAPI document, so the frontend's copy stays current (#463).
        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        output.ShouldNotContain("The_frontends_copy_of_the_openapi_document", customMessage: output);
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0", customMessage: output);
    }

    /// <summary>A project of the profile with the authorization choice; fails while one of its packs is a stand-in.</summary>
    private static GeneratedProject NewProject(string profile, string authorization)
    {
        var project = new GeneratedProject(profile, authorization);
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

    private static string Policies(GeneratedProject project) =>
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "Modules", "Billing", "Billing.Api", "BillingPolicies.cs"));

    private static string PolicyNames(GeneratedProject project) =>
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "Modules", "Billing", "Billing.Application", "BillingPolicyNames.cs"));

    private static JsonNode Realm(string profile, string authorization)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, [authorization]);
        generated.Errors.ShouldBeEmpty();
        return JsonNode.Parse(generated.Files.Single(file => file.Path == "src/AppHost/realms/realm.json").Content)!;
    }

    private static Dictionary<string, JsonNode> Users(JsonNode realm) =>
        realm["users"]!.AsArray().ToDictionary(user => (string)user!["username"]!, user => user!);

    private static string? Department(JsonNode user) =>
        (string?)user["attributes"]?["department"]?.AsArray().Single();

    private static List<string?> Roles(JsonNode user) =>
        user["realmRoles"]?.AsArray().Select(role => (string?)role).ToList() ?? [];
}

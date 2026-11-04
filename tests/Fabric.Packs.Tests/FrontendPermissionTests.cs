using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #75: with <c>backend.authorization</c> roles or claims, the oidc-client-ts pack gives the frontend
/// <c>useHasRole(role)</c>, <c>useHasClaim(type, value?)</c> and <c>&lt;RequirePermission&gt;</c>, read from the ID token
/// (<c>useAuth().user.profile</c>), and the Keycloak realm puts realm roles and <c>department</c> into the ID token.
/// Owner decisions: https://github.com/smartsolutionslab/software-fabric/issues/75#issuecomment-6629903567.
/// </summary>
public sealed class FrontendPermissionTests
{
    private const string Profile = "lean-crud";
    private const string Roles = "backend.authorization=roles";
    private const string Claims = "backend.authorization=claims";
    private const string RolesAndClaims = "backend.authorization=roles,claims";

    private const string UseHasRole = "web/app/src/auth/useHasRole.ts";
    private const string UseHasClaim = "web/app/src/auth/useHasClaim.ts";
    private const string RequirePermission = "web/app/src/auth/RequirePermission.tsx";
    private const string RequirePermissionTest = "web/app/src/auth/RequirePermission.test.tsx";

    [Fact]
    public void Without_roles_or_claims_the_app_has_no_permission_hooks_and_the_realm_no_id_token_mappers()
    {
        var files = Files(Profile);

        files.Keys.ShouldNotContain(UseHasRole);
        files.Keys.ShouldNotContain(UseHasClaim);
        files.Keys.ShouldNotContain(RequirePermission);
        files.Keys.ShouldNotContain(RequirePermissionTest);
        files["AGENTS.md"].ShouldNotContain("RequirePermission");
        WebMappers(files).Keys.ShouldNotContain("realm roles");
    }

    [Theory]
    [InlineData(Roles, true, false)]
    [InlineData(Claims, false, true)]
    [InlineData(RolesAndClaims, true, true)]
    public void The_app_has_the_hook_of_each_chosen_authorization_and_RequirePermission_with_either(string authorization, bool hasRole, bool hasClaim)
    {
        var files = Files(Profile, authorization);

        files.ContainsKey(UseHasRole).ShouldBe(hasRole);
        files.ContainsKey(UseHasClaim).ShouldBe(hasClaim);
        files.Keys.ShouldContain(RequirePermission);
        files.Keys.ShouldContain(RequirePermissionTest);
    }

    [Theory]
    [InlineData(Roles)]
    [InlineData(RolesAndClaims)]
    public void With_roles_the_realm_puts_the_realm_roles_into_the_id_token(string authorization)
    {
        var mapper = WebMappers(Files(Profile, authorization))["realm roles"];

        ((string?)mapper["protocolMapper"]).ShouldBe("oidc-usermodel-realm-role-mapper");
        ((string?)mapper["config"]!["claim.name"]).ShouldBe("realm_access.roles");
        ((string?)mapper["config"]!["multivalued"]).ShouldBe("true");
        ((string?)mapper["config"]!["id.token.claim"]).ShouldBe("true");
    }

    [Fact]
    public void With_claims_alone_the_realm_puts_no_realm_roles_into_the_id_token()
    {
        WebMappers(Files(Profile, Claims)).Keys.ShouldNotContain("realm roles");
    }

    [Theory]
    [InlineData(Claims)]
    [InlineData(RolesAndClaims)]
    public void With_claims_the_realm_puts_the_department_into_the_id_token_as_well_as_the_access_token(string authorization)
    {
        var mapper = WebMappers(Files(Profile, authorization))["department"];

        ((string?)mapper["config"]!["id.token.claim"]).ShouldBe("true");
        ((string?)mapper["config"]!["access.token.claim"]).ShouldBe("true");
    }

    [Theory]
    [InlineData(Roles, "useHasRole")]
    [InlineData(Claims, "useHasClaim")]
    public void The_agent_rules_say_hiding_is_a_convenience_and_the_backend_policy_decides(string authorization, string hook)
    {
        var agents = Files(Profile, authorization)["AGENTS.md"];

        agents.ShouldContain("Hiding an element is a convenience, never protection: the backend policy decides.");
        agents.ShouldContain(hook);
        agents.ShouldContain("<RequirePermission");
    }

    [Theory]
    [InlineData(Roles, "an element that requires the manager role is shown to the developer")]
    [InlineData(Claims, "an element that requires the finance department is shown to the analyst")]
    [InlineData(RolesAndClaims, "an element that requires the manager role and the finance department is shown to the developer")]
    public void The_web_app_installs_builds_lints_is_formatted_and_its_permission_tests_pass(string authorization, string permissionTest)
    {
        using var project = new GeneratedProject(Profile, authorization);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");

        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        testOutput.ShouldContain($"✓ src/auth/RequirePermission.test.tsx > {permissionTest}", customMessage: testOutput);

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static Dictionary<string, JsonNode> WebMappers(Dictionary<string, string> files)
    {
        var realm = JsonNode.Parse(files["src/AppHost/realms/realm.json"])!;
        var web = realm["clients"]!.AsArray().Single(client => (string?)client!["clientId"] == "shop-web")!;
        return web["protocolMappers"]!.AsArray().ToDictionary(mapper => (string)mapper!["name"]!, mapper => mapper!);
    }
}

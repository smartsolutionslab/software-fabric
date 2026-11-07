using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.auth: generic-oidc</c> (roadmap M3-25): the host accepts the access tokens of any OpenID Connect
/// provider and reads only standard claims from them. Locally, and in these tests, Keycloak plays that provider; the
/// host, the modules and the browser reach it through OpenID Connect alone.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class GenericOidcTests
{
    private const string Profile = "saas-standard-react";
    private const string GenericOidc = "backend.auth=generic-oidc";
    private const string RolesAndClaims = "backend.authorization=roles,claims";
    private const string SignalR = "frontend.realtime=signalr";

    private const string NamesKeycloak = """
        namespace Shop.Host;

        public static class RealmRoles
        {
            public const string Claim = "realm_access";
        }
        """;

    [Theory]
    [InlineData("lean-crud")]
    [InlineData(Profile)]
    public void A_project_with_generic_oidc_generates_from_the_real_packs_alone(string profile)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, [GenericOidc, RolesAndClaims, SignalR]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_host_takes_its_authority_and_audience_from_configuration_and_names_no_provider()
    {
        var files = Files(GenericOidc, RolesAndClaims, SignalR);

        var named = files
            .Where(file => file.Key.StartsWith("src/", StringComparison.Ordinal) && !file.Key.StartsWith("src/AppHost/", StringComparison.Ordinal))
            .SelectMany(file => file.Value.Split('\n').Select((line, index) => (file.Key, Number: index + 1, Line: line)))
            .Where(entry => ProviderSpecific().IsMatch(entry.Line))
            .Select(entry => $"{entry.Key}:{entry.Number}: {entry.Line.Trim()}")
            .ToList();
        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));

        files["src/AppHost/AppHost.cs"].ShouldContain("Authentication__Authority");
        files["src/AppHost/AppHost.cs"].ShouldContain("Authentication__Audience");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("mailpit");
        files["src/Host/Wiring/Authentication.cs"].ShouldContain("Authentication:Authority");
        files["src/Host/Wiring/Authentication.cs"].ShouldContain("Authentication:Audience");
    }

    [Fact]
    public void The_local_provider_issues_standard_claims_to_the_test_users()
    {
        var realm = JsonNode.Parse(Files(GenericOidc, RolesAndClaims)["src/AppHost/realms/realm.json"])!;
        var web = realm["clients"]!.AsArray().Single(client => (string?)client!["clientId"] == "shop-web")!;
        var claims = web["protocolMappers"]!.AsArray()
            .Select(mapper => mapper!["config"]!)
            .Where(config => config["claim.name"] is not null)
            .ToDictionary(config => (string)config["claim.name"]!, config => config);

        claims.Keys.ShouldBe(["tenant", "department", "roles"], ignoreOrder: true);
        ((string?)claims["roles"]["access.token.claim"]).ShouldBe("true");
        ((string?)claims["roles"]["id.token.claim"]).ShouldBe("true");
        ((string?)claims["tenant"]["full.path"]).ShouldBe("false");

        var users = realm["users"]!.AsArray().ToDictionary(user => (string)user!["username"]!);
        users.Keys.ShouldBe(["developer", "developer-b", "visitor", "clerk", "analyst"], ignoreOrder: true);
        users["developer-b"]!["groups"]!.AsArray().Select(group => (string?)group).ShouldBe(["/tenant-b"]);
        users["visitor"]!["groups"]!.AsArray().Select(group => (string?)group).ShouldBe(["/tenant-a"]);
    }

    [Fact]
    public void The_architecture_tests_pass_and_fail_once_the_host_names_keycloak()
    {
        using var project = NewProject(GenericOidc, RolesAndClaims, SignalR);
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        project.Write("src/Host/RealmRoles.cs", NamesKeycloak);
        project.Build();

        var (namedExitCode, namedOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        namedExitCode.ShouldNotBe(0, namedOutput);
        namedOutput.ShouldContain("src/Host/RealmRoles.cs:5: names realm_access", Case.Sensitive, namedOutput);
    }

    [Fact]
    public void The_web_app_installs_builds_lints_and_its_permission_tests_read_the_roles_claim()
    {
        using var project = NewProject(GenericOidc, RolesAndClaims);
        var web = Path.Combine(project.ProjectDirectory, "web");
        File.ReadAllText(Path.Combine(web, "app", "src", "auth", "useHasRole.ts")).ShouldNotContain("realm_access");

        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, formatOutput) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");
    }

    /// <summary>
    /// The M3-25 gate: against the running stack, a test user signs in at the local provider, the host knows them, and
    /// their tenant, roles and department decide what they may do, as with Keycloak.
    /// </summary>
    [Fact]
    public void A_test_user_signs_in_and_the_integration_tests_pass_against_the_running_stack()
    {
        using var project = NewProject(GenericOidc, RolesAndClaims, SignalR);
        project.AddModule("billing");
        project.Build();

        // The new module's sample endpoint makes the frontend's OpenAPI copy stale; that test fails and rewrites it.
        var (_, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        FailedCount().Match(output).Groups[1].Value.ShouldBe("1", customMessage: output);
        FailedTest().Matches(output).Select(match => match.Groups[1].Value)
            .ShouldBe(["Shop.Integration.Tests.OpenApiDocumentTests.The_frontends_copy_of_the_openapi_document_is_current"], customMessage: output);
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests", "Integration.Tests", "HostTests.cs"))
            .ShouldContain("A_token_from_the_realm_identifies_the_user");
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A project of the profile with the overrides; fails while one of its packs is a stand-in.</summary>
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

    [GeneratedRegex(@"[Kk]eycloak|/realms/|realm_access|resource_access|/tenants/")]
    private static partial Regex ProviderSpecific();

    [GeneratedRegex(@"(?m)^\s*failed: (\d+)\s*$")]
    private static partial Regex FailedCount();

    [GeneratedRegex(@"(?m)^\s*failed (\S+)")]
    private static partial Regex FailedTest();
}

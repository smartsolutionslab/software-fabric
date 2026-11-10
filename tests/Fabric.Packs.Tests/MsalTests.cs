using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.auth-client: msal</c> (roadmap M3-44, #266): the browser signs in at M3-26's stand-in for Entra ID through
/// MSAL, <c>@azure/msal-react</c> on React and a hand-written composable over <c>@azure/msal-browser</c> on Vue (ADR-0006).
/// msal-common refuses an authority that is not https, so with msal the stand-in also serves https with the ASP.NET Core
/// development certificate and the AppHost gives the browser that address (owner decision on #266, option A). There are
/// no end-to-end tests (D8): the web app's tests run MSAL against the generated configuration instead.
/// </summary>
public sealed partial class MsalTests
{
    private const string EntraId = "backend.auth=entra-id";
    private const string Msal = "frontend.auth-client=msal";
    private const string RolesAndClaims = "backend.authorization=roles,claims";
    private const string HttpAuthority = "http://localhost:8080/organizations/v2.0";
    private const string HttpsAuthority = "https://localhost:8443/organizations/v2.0";

    public static TheoryData<string, string[]> Generations => new()
    {
        { "saas-standard-react", [EntraId, Msal] },
        { "saas-standard-vue", [EntraId, Msal] },
        { "lean-crud", [EntraId, Msal, RolesAndClaims, "frontend.realtime=signalr"] },
        { "saas-standard-react", [EntraId, Msal, RolesAndClaims, "frontend.realtime=sse", "backend.test.integration=testcontainers"] },
        { "saas-standard-vue", [EntraId, Msal, "backend.authorization=roles", "frontend.realtime=sse"] },
        { "saas-ai-react", [EntraId, Msal, RolesAndClaims, "backend.mcp=client,server"] },
    };

    [Theory]
    [MemberData(nameof(Generations))]
    public void A_project_with_msal_generates_from_the_real_packs_alone(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void With_msal_the_stand_in_also_serves_https_and_the_apphost_gives_the_browser_that_authority()
    {
        var files = Files("saas-standard-react", EntraId, Msal);

        var launch = JsonNode.Parse(files["src/IdentityStandIn/Properties/launchSettings.json"])!;
        var urls = launch["profiles"]!.AsObject().Select(profile => (string?)profile.Value!["applicationUrl"]).Single()!.Split(';');
        urls.ShouldBe(["https://localhost:8443", "http://localhost:8080"], ignoreOrder: true);

        var wiring = files["src/AppHost/Wiring/StandInWiring.cs"];
        wiring.ShouldContain("""ReferenceExpression.Create($"{identity.GetEndpoint("https")}/organizations/v2.0")""");
        wiring.ShouldContain(""".WithEnvironment("AzureAd__BrowserInstance", identity.GetEndpoint("https"))""");
        wiring.ShouldContain(""".WithEnvironment("AzureAd__Instance", identity.GetEndpoint("http"))""");
        files["src/AppHost/AppHost.cs"].ShouldContain("""endpointName: "http")""");
        files["src/Host/Wiring/Authentication.cs"].ShouldContain("AzureAd:BrowserInstance");

        files["web/app/src/auth/msalConfig.ts"].ShouldContain(HttpsAuthority);
        files.Keys.ShouldContain("tests/Integration.Tests/BrowserIssuerTests.cs");
    }

    [Fact]
    public void With_oidc_client_ts_the_stand_in_serves_http_alone_as_before()
    {
        var files = Files("saas-standard-react", EntraId);

        var launch = JsonNode.Parse(files["src/IdentityStandIn/Properties/launchSettings.json"])!;
        launch["profiles"]!.AsObject().Select(profile => (string?)profile.Value!["applicationUrl"]).ShouldBe(["http://localhost:8080"]);
        files["src/AppHost/Wiring/StandInWiring.cs"].ShouldNotContain("GetEndpoint(\"https\")");
        files["src/AppHost/Wiring/StandInWiring.cs"].ShouldNotContain("AzureAd__BrowserInstance");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("endpointName");
        files["src/Host/Wiring/Authentication.cs"].ShouldNotContain("BrowserInstance");
        files["web/app/src/auth/oidcConfig.ts"].ShouldContain(HttpAuthority);
        files.Keys.ShouldNotContain("tests/Integration.Tests/BrowserIssuerTests.cs");
    }

    [Theory]
    [InlineData("saas-standard-react", "@azure/msal-react")]
    [InlineData("saas-standard-vue", "src/auth/useAuth.ts")]
    public void The_decision_records_and_agent_rules_name_the_development_certificate_and_the_missing_end_to_end_tests(string profile, string client)
    {
        var files = Files(profile, EntraId, Msal);
        var adr = files.Single(file => file.Key.EndsWith("-frontend-auth-client-msal.md", StringComparison.Ordinal)).Value;
        var entraId = files.Single(file => file.Key.EndsWith("-backend-auth-entra-id.md", StringComparison.Ordinal)).Value;
        var agents = files["AGENTS.md"];

        adr.ShouldContain(client);
        adr.ShouldContain("No end-to-end tests");
        adr.ShouldContain("dotnet dev-certs https --trust");
        adr.ShouldContain("responseMode");
        entraId.ShouldContain("dotnet dev-certs https --trust");
        entraId.ShouldContain("AzureAd:BrowserInstance");
        agents.ShouldContain("## Browser sign-in: MSAL");
        agents.ShouldContain("dotnet dev-certs https --trust");
        agents.ShouldNotContain("react-oidc-context");
        agents.ShouldNotContain("oidc-client-ts");
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    public void The_web_app_has_no_oidc_client_ts_and_no_end_to_end_sign_in(string profile)
    {
        var files = Files(profile, EntraId, Msal);
        var package = JsonNode.Parse(files["web/app/package.json"])!["dependencies"]!.AsObject();

        package.Select(dependency => dependency.Key).ShouldContain("@azure/msal-browser");
        ((string?)package["@azure/msal-browser"]).ShouldBe("5.27.0");
        package.Select(dependency => dependency.Key).ShouldNotContain("oidc-client-ts");
        package.Select(dependency => dependency.Key).ShouldNotContain("react-oidc-context");
        if (profile.EndsWith("react", StringComparison.Ordinal))
        {
            ((string?)package["@azure/msal-react"]).ShouldBe("5.7.4");
        }
        else
        {
            package.Select(dependency => dependency.Key).ShouldNotContain("@azure/msal-react");
        }

        files.Keys.ShouldNotContain("web/app/e2e/sign-in.spec.ts");
        var named = files.Where(file => file.Key.StartsWith("web/app/src/", StringComparison.Ordinal) && OidcClient().IsMatch(file.Value))
            .Select(file => file.Key)
            .ToList();
        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    /// <summary>The gate: the web app's own tests run MSAL against the configuration it ships, and the sign-in screens.</summary>
    [Theory]
    [InlineData("saas-standard-react", "src/auth/SignedIn.test.tsx", "src/auth/RequirePermission.test.tsx")]
    [InlineData("saas-standard-vue", "src/auth/SignedIn.test.ts", "src/auth/RequirePermission.test.ts")]
    public void The_web_app_installs_builds_lints_is_formatted_and_its_sign_in_tests_pass(string profile, string signedInTest, string permissionTest)
    {
        using var project = new GeneratedProject(profile, EntraId, Msal, RolesAndClaims);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");

        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");
        var plain = Plain(testOutput);
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        plain.ShouldContain("✓ src/auth/msalConfig.test.ts > the authority is https, the only kind msal-common accepts", customMessage: testOutput);
        plain.ShouldContain("✓ src/auth/msalConfig.test.ts > msal-browser accepts the configuration and sends the user to the stand-in, which answers in the query", customMessage: testOutput);
        plain.ShouldContain("✓ src/auth/msalConfig.test.ts > coming back from the stand-in, msal-browser signs the user in and the access token is kept", customMessage: testOutput);
        plain.ShouldContain($"✓ {signedInTest} > an anonymous visitor is sent to the identity provider", customMessage: testOutput);
        plain.ShouldContain($"✓ {signedInTest} > a session whose access token expires sends the user back to the identity provider", customMessage: testOutput);
        plain.ShouldContain($"✓ {permissionTest} > an element that requires the manager role and the finance department is shown to the developer", customMessage: testOutput);

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        // A non-https authority cannot slip back in: built with the stand-in's http address, the same tests fail.
        var (httpExitCode, httpOutput) = Shell.Run(app, new Dictionary<string, string> { ["VITE_OIDC_AUTHORITY"] = HttpAuthority }, "pnpm", "test", "--reporter=verbose");
        httpExitCode.ShouldNotBe(0, httpOutput);
        Plain(httpOutput).ShouldContain("× src/auth/msalConfig.test.ts > the authority is https, the only kind msal-common accepts", customMessage: httpOutput);
        httpOutput.ShouldContain("authority_uri_insecure", customMessage: httpOutput);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>CI forces colour, which puts escape codes between the parts of a reported test's name.</summary>
    private static string Plain(string output) => Regex.Replace(output, @"\x1B\[[0-9;]*m", string.Empty);

    [GeneratedRegex(@"oidc-client-ts|react-oidc-context|oidc\.user:")]
    private static partial Regex OidcClient();
}

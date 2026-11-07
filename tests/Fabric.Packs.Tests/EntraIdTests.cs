using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.auth: entra-id</c> (roadmap M3-26): the host accepts Microsoft Entra ID access tokens from the directories
/// it lists, one per tenant, and reads the tenant from <c>tid</c>. Locally, and in these tests, an in-process stand-in
/// issuer plays Entra ID; only the AppHost knows it. There are no end-to-end tests (D8).
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class EntraIdTests
{
    private const string Profile = "saas-standard-react";
    private const string EntraId = "backend.auth=entra-id";
    private const string RolesAndClaims = "backend.authorization=roles,claims";
    private const string SignalR = "frontend.realtime=signalr";
    private const string Testcontainers = "backend.test.integration=testcontainers";
    private const string TenantA = "00000000-0000-0000-0000-00000000000a";
    private const string TenantB = "00000000-0000-0000-0000-00000000000b";
    private const string StandInProject = "src/IdentityStandIn/IdentityStandIn.csproj";

    private const string NamesTheStandIn = """
        <ProjectReference Include="../../../src/IdentityStandIn/IdentityStandIn.csproj" />
          </ItemGroup>
        """;

    public static TheoryData<string, string[]> Generations => new()
    {
        { "lean-crud", [EntraId, RolesAndClaims, SignalR] },
        { Profile, [EntraId, RolesAndClaims, SignalR] },
        { Profile, [EntraId, RolesAndClaims, SignalR, Testcontainers] },
        { "saas-ai-react", [EntraId, RolesAndClaims, "backend.mcp=client,server"] },
    };

    [Theory]
    [MemberData(nameof(Generations))]
    public void A_project_with_entra_id_generates_from_the_real_packs_alone(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_host_accepts_the_listed_directories_and_only_the_apphost_knows_the_stand_in()
    {
        var files = Files(EntraId, RolesAndClaims, SignalR);

        var named = files
            .Where(file => file.Key.StartsWith("src/", StringComparison.Ordinal)
                && !file.Key.StartsWith("src/AppHost/", StringComparison.Ordinal)
                && !file.Key.StartsWith("src/IdentityStandIn/", StringComparison.Ordinal))
            .SelectMany(file => file.Value.Split('\n').Select((line, index) => (file.Key, Number: index + 1, Line: line)))
            .Where(entry => ProviderSpecific().IsMatch(entry.Line))
            .Select(entry => $"{entry.Key}:{entry.Number}: {entry.Line.Trim()}")
            .ToList();
        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));

        files.Keys.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal) && files[path].Contains("IdentityStandIn.csproj", StringComparison.Ordinal))
            .ShouldBe(["src/AppHost/AppHost.csproj"]);
        files.ShouldContainKey(StandInProject);

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("AddProject<Projects.IdentityStandIn>(\"identity\")");
        appHost.ShouldContain("\"AzureAd__Audience\", \"api://shop-api\"");
        appHost.ShouldContain($"\"AzureAd__Tenants__tenant-a\", \"{TenantA}\"");
        appHost.ShouldContain($"\"AzureAd__Tenants__tenant-b\", \"{TenantB}\"");
        appHost.ShouldNotContain("Keycloak");

        var authentication = files["src/Host/Wiring/Authentication.cs"];
        authentication.ShouldContain("AzureAd:Instance");
        authentication.ShouldContain("AzureAd:Audience");
        authentication.ShouldContain("AzureAd:Tenants");
        authentication.ShouldContain("ValidIssuers");
        authentication.ShouldContain("MapInboundClaims = false");
        authentication.ShouldNotContain("Microsoft.Identity.Web");

        files["src/BuildingBlocks/Tenancy/HttpTenantContext.cs"].ShouldContain("\"tid\"");
        files["src/Host/Program.cs"].ShouldContain("preferred_username");
    }

    [Fact]
    public void The_stand_in_knows_the_test_users_by_their_directories_roles_and_departments()
    {
        var settings = JsonNode.Parse(Files(EntraId, RolesAndClaims)["src/IdentityStandIn/appsettings.json"])!["StandIn"]!;

        ((string?)settings["Audience"]).ShouldBe("api://shop-api");
        ((string?)settings["Client"]).ShouldBe("shop-web");
        ((string?)settings["Directories"]!["tenant-a"]).ShouldBe(TenantA);
        ((string?)settings["Directories"]!["tenant-b"]).ShouldBe(TenantB);

        var users = settings["Users"]!.AsObject().ToDictionary(user => user.Key, user => user.Value!);
        users.Keys.ShouldBe(["developer", "developer-b", "visitor", "clerk", "analyst", "outsider"], ignoreOrder: true);
        ((string?)users["developer-b"]["Directory"]).ShouldBe("tenant-b");
        ((string?)users["visitor"]["Directory"]).ShouldBe("tenant-a");
        users["developer"]["Roles"]!.AsArray().Select(role => (string?)role).ShouldBe(["manager"]);
        users["visitor"]["Roles"]!.AsArray().ShouldBeEmpty();
        ((string?)users["analyst"]["Department"]).ShouldBe("finance");
        ((string?)users["outsider"]["Directory"]).ShouldBe("elsewhere");
        settings["Directories"]!["elsewhere"].ShouldNotBeNull();
    }

    [Fact]
    public void The_decision_records_and_the_permission_tests_describe_entra_ids_claims_and_the_missing_end_to_end_tests()
    {
        var files = Files(EntraId, RolesAndClaims);
        var adr = files.Single(file => file.Key.EndsWith("-backend-auth-entra-id.md", StringComparison.Ordinal)).Value;
        var tenancy = files.Single(file => file.Key.EndsWith("-backend-tenancy-column.md", StringComparison.Ordinal)).Value;
        var roles = files.Single(file => file.Key.EndsWith("-backend-authorization-roles.md", StringComparison.Ordinal)).Value;
        var permissionTests = files["web/app/src/auth/RequirePermission.test.tsx"];

        adr.ShouldContain("No end-to-end tests");
        adr.ShouldContain("`tid`");
        adr.ShouldContain("AzureAd:Tenants");
        tenancy.ShouldContain("`tid`");
        tenancy.ShouldNotContain("/tenants/");
        roles.ShouldContain("`EntraRoleClaims`");
        roles.ShouldContain("`src/Host/EntraRoleClaims.cs`");
        roles.ShouldNotContain("KeycloakRoleClaims");
        permissionTests.ShouldNotContain("realm_access");
        permissionTests.ShouldNotContain("Keycloak");
    }

    /// <summary>
    /// The stand-in, run alone: its discovery documents, the password grant the tests use, and the authorization code
    /// flow with PKCE that the browser uses, signed with the key its JWKS publishes.
    /// </summary>
    [Fact]
    public async Task The_stand_in_issues_entra_shaped_tokens_by_password_and_by_code_with_pkce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var project = NewProject(EntraId, RolesAndClaims);
        var (buildExitCode, buildOutput) = project.DotNetCommand(new Dictionary<string, string>(), "build", StandInProject, "-c", "Release");
        buildExitCode.ShouldBe(0, buildOutput);
        using var standIn = StandIn.Start(project.ProjectDirectory);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = standIn.Address };
        await standIn.WaitUntilReadyAsync(http, cancellationToken);
        var issuerBase = standIn.Address.ToString().TrimEnd('/');

        var metadata = await http.GetFromJsonAsync<JsonObject>(new Uri("/organizations/v2.0/.well-known/openid-configuration", UriKind.Relative), cancellationToken);
        var tokenEndpoint = new Uri((string)metadata!["token_endpoint"]!);
        var keys = await http.GetFromJsonAsync<JsonObject>(new Uri((string)metadata["jwks_uri"]!), cancellationToken);
        var keyIds = keys!["keys"]!.AsArray().Select(key => (string?)key!["kid"]).ToList();
        var tenantMetadata = await http.GetFromJsonAsync<JsonObject>(new Uri($"/{TenantB}/v2.0/.well-known/openid-configuration", UriKind.Relative), cancellationToken);
        ((string?)tenantMetadata!["issuer"]).ShouldBe($"{issuerBase}/{TenantB}/v2.0");

        using var passwordResponse = await http.PostAsync(tokenEndpoint, Form(("grant_type", "password"), ("client_id", "shop-web"), ("username", "developer-b"), ("password", "developer-b")), cancellationToken);
        var passwordBody = await passwordResponse.Content.ReadAsStringAsync(cancellationToken);
        passwordResponse.StatusCode.ShouldBe(HttpStatusCode.OK, passwordBody);
        var accessToken = (string)JsonNode.Parse(passwordBody)!["access_token"]!;
        var (header, claims) = Decode(accessToken);
        keyIds.ShouldContain((string?)header["kid"]);
        ((string?)claims["iss"]).ShouldBe($"{issuerBase}/{TenantB}/v2.0");
        ((string?)claims["tid"]).ShouldBe(TenantB);
        ((string?)claims["aud"]).ShouldBe("api://shop-api");
        ((string?)claims["preferred_username"]).ShouldBe("developer-b");
        claims["roles"]!.AsArray().Select(role => (string?)role).ShouldBe(["manager"]);
        ((string?)claims["department"]).ShouldBe("finance");

        using var shortLived = await http.PostAsync(tokenEndpoint, Form(("grant_type", "password"), ("client_id", "shop-web"), ("username", "developer"), ("password", "developer"), ("lifetime", "10")), cancellationToken);
        var (_, shortClaims) = Decode((string)JsonNode.Parse(await shortLived.Content.ReadAsStringAsync(cancellationToken))!["access_token"]!);
        ((long)shortClaims["exp"]! - (long)shortClaims["iat"]!).ShouldBe(10);

        using var wrongPassword = await http.PostAsync(tokenEndpoint, Form(("grant_type", "password"), ("client_id", "shop-web"), ("username", "developer"), ("password", "wrong")), cancellationToken);
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        const string Verifier = "a-code-verifier-that-is-long-enough-for-pkce-0123456789";
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
        var authorize = $"/organizations/oauth2/v2.0/authorize?client_id=shop-web&redirect_uri={Uri.EscapeDataString("http://localhost:5173/")}&response_type=code&scope=openid&state=the-state&nonce=the-nonce&code_challenge={challenge}&code_challenge_method=S256";
        using var page = await http.GetAsync(new Uri(authorize, UriKind.Relative), cancellationToken);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        var form = await page.Content.ReadAsStringAsync(cancellationToken);
        form.ShouldContain("<label for=\"username\">Username</label>");
        form.ShouldContain("<label for=\"password\">Password</label>");
        form.ShouldContain("Sign in");

        using var signIn = await http.PostAsync(new Uri(authorize, UriKind.Relative), Form(("username", "developer"), ("password", "developer")), cancellationToken);
        signIn.StatusCode.ShouldBe(HttpStatusCode.Redirect, await signIn.Content.ReadAsStringAsync(cancellationToken));
        var redirect = signIn.Headers.Location!;
        redirect.GetLeftPart(UriPartial.Path).ShouldBe("http://localhost:5173/");
        var query = System.Web.HttpUtility.ParseQueryString(redirect.Query);
        query["state"].ShouldBe("the-state");
        var code = query["code"].ShouldNotBeNull();

        using var wrongVerifier = await http.PostAsync(tokenEndpoint, Form(("grant_type", "authorization_code"), ("client_id", "shop-web"), ("code", code), ("redirect_uri", "http://localhost:5173/"), ("code_verifier", "not-the-verifier")), cancellationToken);
        wrongVerifier.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var exchanged = await http.PostAsync(tokenEndpoint, Form(("grant_type", "authorization_code"), ("client_id", "shop-web"), ("code", code), ("redirect_uri", "http://localhost:5173/"), ("code_verifier", Verifier)), cancellationToken);
        var exchangedBody = await exchanged.Content.ReadAsStringAsync(cancellationToken);
        exchanged.StatusCode.ShouldBe(HttpStatusCode.OK, exchangedBody);
        var (_, idClaims) = Decode((string)JsonNode.Parse(exchangedBody)!["id_token"]!);
        ((string?)idClaims["aud"]).ShouldBe("shop-web");
        ((string?)idClaims["nonce"]).ShouldBe("the-nonce");
        ((string?)idClaims["tid"]).ShouldBe(TenantA);
        idClaims["roles"]!.AsArray().Select(role => (string?)role).ShouldBe(["manager"]);

        using var foreignRedirect = await http.PostAsync(
            new Uri(authorize.Replace(Uri.EscapeDataString("http://localhost:5173/"), Uri.EscapeDataString("http://evil.example/"), StringComparison.Ordinal), UriKind.Relative),
            Form(("username", "developer"), ("password", "developer")),
            cancellationToken);
        foreignRedirect.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void The_architecture_tests_pass_and_fail_once_another_project_references_the_stand_in()
    {
        using var project = NewProject(EntraId, RolesAndClaims, SignalR);
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        const string Tests = "tests/Modules/Billing.Tests/Billing.Tests.csproj";
        var csproj = File.ReadAllText(Path.Combine(project.ProjectDirectory, Tests));
        var lastItemGroup = csproj.LastIndexOf("  </ItemGroup>", StringComparison.Ordinal);
        project.Write(Tests, string.Concat(csproj.AsSpan(0, lastItemGroup), "  " + NamesTheStandIn, csproj.AsSpan(lastItemGroup + "  </ItemGroup>".Length)));
        project.Build();

        var (namedExitCode, namedOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        namedExitCode.ShouldNotBe(0, namedOutput);
        namedOutput.ShouldContain($"{Tests} references src/IdentityStandIn", Case.Sensitive, namedOutput);
    }

    /// <summary>
    /// The stand-in is for development and tests only (review of #320): a published AppHost has no stand-in, and gives
    /// the host and the browser none of its settings, which production configures for Entra ID itself.
    /// </summary>
    [Fact]
    public void Publishing_the_apphost_leaves_the_stand_in_and_its_settings_out()
    {
        using var project = new GeneratedProject("lean-crud", EntraId);
        var (buildExitCode, buildOutput) = project.DotNetCommand(new Dictionary<string, string>(), "build", "src/AppHost/AppHost.csproj", "-c", "Release");
        buildExitCode.ShouldBe(0, buildOutput);
        var output = Path.Combine(project.ProjectDirectory, "publish");

        var (exitCode, publishOutput) = DotNet.Run(
            Path.Combine(project.ProjectDirectory, "src", "AppHost", "bin", "Release", "net10.0"),
            new Dictionary<string, string> { ["DOTNET_ENVIRONMENT"] = "Development" },
            "AppHost.dll",
            "--operation",
            "publish",
            "--step",
            "publish-manifest",
            "--output-path",
            output);
        exitCode.ShouldBe(0, publishOutput);

        var resources = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "aspire-manifest.json")))!["resources"]!.AsObject();
        resources.Select(resource => resource.Key).ShouldNotContain("identity");
        var published = resources.SelectMany(resource => resource.Value!["env"]?.AsObject().Select(variable => $"{resource.Key}: {variable.Key}={variable.Value}") ?? [])
            .Where(variable => variable.Contains("AzureAd", StringComparison.Ordinal) || variable.Contains("VITE_OIDC_AUTHORITY", StringComparison.Ordinal) || variable.Contains("identity", StringComparison.Ordinal))
            .ToList();
        published.ShouldBeEmpty(string.Join(Environment.NewLine, published));
    }

    /// <summary>
    /// The M3-26 gate: against the running stack, test users sign in at the stand-in, the host accepts their tokens and
    /// knows them, their tenant, roles and department decide what they may do, and a directory the host does not list is
    /// refused.
    /// </summary>
    [Fact]
    public void A_test_user_signs_in_and_the_integration_tests_pass_against_the_running_stack()
    {
        using var project = NewProject(EntraId, RolesAndClaims, SignalR);
        project.AddModule("billing");
        project.Build();

        // The new module's sample endpoint makes the frontend's OpenAPI copy stale; that test fails and rewrites it.
        var (_, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        FailedCount().Match(output).Groups[1].Value.ShouldBe("1", customMessage: output);
        FailedTest().Matches(output).Select(match => match.Groups[1].Value)
            .ShouldBe(["Shop.Integration.Tests.OpenApiDocumentTests.The_frontends_copy_of_the_openapi_document_is_current"], customMessage: output);
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests", "Integration.Tests", "DirectoryTests.cs"))
            .ShouldContain("A_token_from_a_directory_the_host_does_not_list_is_refused");
    }

    /// <summary>The same gate on Testcontainers: the stand-in in a container of its own, the host in process.</summary>
    [Fact]
    public void A_test_user_signs_in_and_the_integration_tests_pass_on_testcontainers()
    {
        using var project = NewProject(EntraId, RolesAndClaims, SignalR, Testcontainers);
        project.AddModule("billing");
        project.Build();

        var (_, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        FailedCount().Match(output).Groups[1].Value.ShouldBe("1", customMessage: output);
        FailedTest().Matches(output).Select(match => match.Groups[1].Value)
            .ShouldBe(["Shop.Integration.Tests.OpenApiDocumentTests.The_frontends_copy_of_the_openapi_document_is_current"], customMessage: output);
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

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static (JsonObject Header, JsonObject Claims) Decode(string token)
    {
        var parts = token.Split('.');
        parts.Length.ShouldBe(3);
        return (Part(parts[0]), Part(parts[1]));

        static JsonObject Part(string part)
        {
            var base64 = part.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            return JsonNode.Parse(Convert.FromBase64String(base64))!.AsObject();
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex(@"[Kk]eycloak|/realms/|realm_access|resource_access|/tenants/|IdentityStandIn|StandIn")]
    private static partial Regex ProviderSpecific();

    [GeneratedRegex(@"(?m)^\s*failed: (\d+)\s*$")]
    private static partial Regex FailedCount();

    [GeneratedRegex(@"(?m)^\s*failed (\S+)")]
    private static partial Regex FailedTest();

    /// <summary>The built stand-in, started alone in Development on a free port.</summary>
    private sealed class StandIn : IDisposable
    {
        private readonly Process process;
        private readonly StringBuilder output = new();

        private StandIn(Process process, Uri address)
        {
            this.process = process;
            Address = address;
            process.OutputDataReceived += (_, line) => Record(line.Data);
            process.ErrorDataReceived += (_, line) => Record(line.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public Uri Address { get; }

        public static StandIn Start(string projectDirectory)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.Combine(projectDirectory, "src", "IdentityStandIn", "bin", "Release", "net10.0"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("IdentityStandIn.dll");
            start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            return new StandIn(Process.Start(start)!, new Uri($"http://127.0.0.1:{port}"));
        }

        public async Task WaitUntilReadyAsync(HttpClient http, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    using var response = await http.GetAsync(new Uri("/organizations/v2.0/.well-known/openid-configuration", UriKind.Relative), cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }

                if (process.HasExited)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }

            Dispose();
            lock (output)
            {
                throw new InvalidOperationException($"The stand-in did not start:{Environment.NewLine}{output}");
            }
        }

        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            process.Dispose();
        }

        private void Record(string? line)
        {
            lock (output)
            {
                output.AppendLine(line);
            }
        }
    }
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The identity-provider vocabulary (docs/slots.md, M3-13): what depends on the identity provider (where its roles sit
/// in the token, which tenant its user belongs to, how a test user signs in, its login page, the shape of its ID token)
/// comes from <c>templates.functions</c> of the <c>backend.auth</c> pack, and how the browser holds its access token
/// from those of the <c>frontend.auth-client</c> pack, so generic-oidc, entra-id and msal plug in by defining the same
/// functions instead of editing roles, claims, tenancy, signalr, ag-ui, the micro-frontends and the base's policy rule.
/// </summary>
public sealed partial class IdentityVocabularyTests
{
    private static readonly string[] IdentityProviderVocabulary =
    [
        "role_mapping_source",
        "unstarted_host_settings",
        "test_user_token",
        "test_user_token_types",
        "short_lived_test_user_token",
        "short_lived_test_user_token_members",
        "short_lived_test_user_token_types",
        "short_lived_test_user_token_namespaces",
        "accept_query_token",
        "accept_query_token_namespaces",
        "browser_sign_in",
        "browser_authority",
        "id_token_roles",
        "id_token_has_role",
        "id_token_roles_claim",
        "id_token_users_comment",
        "role_mapping_adr",
        "role_mapping_provider",
        "tenant_claim_adr",
        "tenant_claim",
        "tenant_claim_members",
        "tenant_claim_description",
        "user_name_claim",
        "browser_client_settings",
        "browser_client_env",
        "local_provider",
        "local_user",
        "local_users",
        "local_users_source",
        "local_users_setup",
        "identity_resource",
        "short_lived_test_user_token_summary",
        "unstarted_people_resources_adr",
        "browser_client_adr",
        "id_token_claims_adr",
        "id_token_claim_rule",
        "role_kind",
        "add_role_rule",
        "roles_production_adr",
        "token_issuer",
        "claim_attribute_owner",
        "claim_mapping_adr",
        "claim_change_adr",
        "add_claim_rule",
    ];

    private static readonly string[] AuthClientVocabulary =
    [
        "browser_access_token_import",
        "browser_access_token",
        "stored_access_token",
        "store_access_token_import",
        "store_access_token",
    ];

    private static readonly string[] IntegrationVocabulary =
    [
        "integration_fixture",
        "host_client",
        "signed_in_host_client",
        "running_application",
        "database_fixture",
        "database_connection_string",
        "database_fixture_file",
        "database_fixture_package_references",
        "database_fixture_project_references",
        "integration_fixture_attributes",
        "database_fixture_attributes",
    ];

    /// <summary>
    /// The files that may name Keycloak: the composition root and the host's shared files, which hold the keycloak
    /// pack's slot contributions, and the integration pack's host tests, which carry Keycloak's own Mailpit gate.
    /// The keycloak pack's own targets are added from its manifest.
    /// </summary>
    private static readonly string[] SharedFiles =
    [
        "src/Host/Program.cs",
        "Directory.Packages.props",
        "tests/Integration.Tests/HostTests.cs",
    ];

    private static readonly string[] CheckedDirectories = ["src", "tests", "web"];

    /// <summary>
    /// Besides oidc-client-ts's own targets (added from its manifest), the files that may name how it holds the token:
    /// the API client's headers, which it fills.
    /// </summary>
    private static readonly string[] AuthClientFiles =
    [
        "web/app/src/api/baseApi.ts",
    ];

    /// <summary>
    /// Every user of the vocabulary: roles and claims with their tests, column tenancy, the MCP server, signalr and ag-ui
    /// on React, the Vue sign-in, sse's query token on React and Vue, and the micro-frontends' end-to-end tests.
    /// </summary>
    public static TheoryData<string, string[]> Generations => new()
    {
        { "saas-ai-react", ["backend.authorization=roles,claims", "backend.mcp=client,server", "frontend.realtime=signalr"] },
        { "saas-standard-vue", ["backend.authorization=roles", "frontend.realtime=signalr"] },
        { "saas-standard-react", ["frontend.realtime=sse"] },
        { "saas-standard-vue", ["frontend.realtime=sse"] },
        { "industrial-event-driven-mfe", [] },
    };

    /// <summary>The same users of the auth client's vocabulary with msal (#266), which needs Entra ID.</summary>
    public static TheoryData<string, string[]> MsalGenerations => new()
    {
        { "saas-ai-react", ["backend.auth=entra-id", "frontend.auth-client=msal", "backend.authorization=roles,claims", "backend.mcp=client,server", "frontend.realtime=signalr"] },
        { "saas-standard-vue", ["backend.auth=entra-id", "frontend.auth-client=msal", "backend.authorization=roles", "frontend.realtime=signalr"] },
        { "saas-standard-react", ["backend.auth=entra-id", "frontend.auth-client=msal", "frontend.realtime=sse"] },
        { "saas-standard-vue", ["backend.auth=entra-id", "frontend.auth-client=msal", "frontend.realtime=sse"] },
    };

    [Theory]
    [InlineData("backend.auth")]
    [InlineData("frontend.auth-client")]
    public void Every_identity_pack_of_the_dimension_defines_the_whole_vocabulary_and_nothing_else(string dimension)
    {
        var vocabulary = dimension == "backend.auth" ? IdentityProviderVocabulary : AuthClientVocabulary;
        var packs = Repository.Packs.Packs.Values.Where(pack => pack.Dimension == dimension && pack.OptionName != "none").ToList();
        packs.ShouldNotBeEmpty();

        foreach (var pack in packs)
        {
            VocabularyProbe.Functions(pack).ShouldBe(vocabulary, ignoreOrder: true, customMessage: $"{pack.Option} must define, in {VocabularyProbe.FunctionsSlot}, exactly the vocabulary of docs/slots.md.");
        }
    }

    [Fact]
    public void The_integration_pack_names_the_running_application_for_the_identity_providers_test_users()
    {
        var pack = Repository.Packs.Packs["backend.test.integration.aspire-fixture"];

        VocabularyProbe.Functions(pack).ShouldBe(IntegrationVocabulary, ignoreOrder: true);
    }

    /// <summary>Refusals of another topology name the keycloak pack as their owner; they are not about the provider.</summary>
    [Fact]
    public void No_pack_refuses_another_identity_provider_or_auth_client()
    {
        var refusals = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*.sbn", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => (entry.line.StartsWith("#error", StringComparison.Ordinal) || entry.line.StartsWith("throw new Error(", StringComparison.Ordinal))
                && IdentityOnly().IsMatch(entry.line)
                && !entry.line.Contains("modular-monolith", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.path)}:{entry.index + 1}: {entry.line}")
            .ToList();

        refusals.ShouldBeEmpty(string.Join(Environment.NewLine, refusals));
    }

    [Fact]
    public void An_identity_provider_without_the_vocabulary_fails_generation_naming_what_it_lacks()
    {
        string[] overrides = ["backend.auth=generic-oidc", "backend.authorization=roles,claims", "frontend.realtime=signalr"];
        var packs = WithoutVocabulary(Repository.Packs.Packs["backend.auth.generic-oidc"]);

        var generated = Generator.New(Repository.Catalog, packs, "shop", "lean-crud", overrides);

        var errors = string.Join(Environment.NewLine, generated.Errors);
        errors.ShouldContain("role_mapping_source", customMessage: errors);
        errors.ShouldContain("test_user_token", customMessage: errors);
        errors.ShouldContain("accept_query_token", customMessage: errors);
    }

    [Fact]
    public void An_identity_provider_without_the_vocabulary_fails_generation_of_column_tenancy_naming_the_tenant_claim()
    {
        string[] overrides = ["backend.auth=generic-oidc"];
        var packs = WithoutVocabulary(Repository.Packs.Packs["backend.auth.generic-oidc"]);

        var generated = Generator.New(Repository.Catalog, packs, "shop", "saas-standard-react", overrides);

        var errors = string.Join(Environment.NewLine, generated.Errors);
        errors.ShouldContain("tenant_claim", customMessage: errors);
    }

    [Fact]
    public void The_endpoint_rule_is_the_bases_for_every_identity_provider()
    {
        const string Rule = "tests/Architecture.Tests/Rules/EveryEndpointDeclaresItsAuthorization.cs";
        var owners = Repository.Packs.Packs.Values
            .Where(pack => pack.Files.Any(file => file.Target == Rule))
            .Select(pack => pack.Option)
            .ToList();

        owners.ShouldBe(["base.core"]);
    }

    [Theory]
    [MemberData(nameof(Generations))]
    public void A_project_generated_with_other_vocabularies_names_keycloak_and_oidc_client_ts_only_in_their_own_files(string profile, string[] overrides)
    {
        var keycloak = Repository.Packs.Packs["backend.auth.keycloak"];
        var oidcClient = Repository.Packs.Packs["frontend.auth-client.oidc-client-ts"];
        using var identityProvider = new VocabularyProbe(keycloak, IdentityProviderVocabulary);
        using var authClient = new VocabularyProbe(oidcClient, AuthClientVocabulary);
        using var integration = new VocabularyProbe(Repository.Packs.Packs["backend.test.integration.aspire-fixture"], IntegrationVocabulary);
        using var project = new GeneratedProject(profile, [identityProvider.Manifest, authClient.Manifest, integration.Manifest], overrides);
        project.AddModule("billing");

        var keycloakFiles = keycloak.Files.Select(file => file.Target).Concat(SharedFiles).ToHashSet(StringComparer.Ordinal);
        var authClientFiles = oidcClient.Files.Select(file => file.Target).Concat(AuthClientFiles).ToHashSet(StringComparer.Ordinal);
        var named = Code(project)
            .Where(entry => !entry.relative.StartsWith("src/AppHost/", StringComparison.Ordinal) && !keycloakFiles.Contains(entry.relative))
            .Where(entry => KeycloakSpecific().IsMatch(entry.line))
            .Concat(Code(project)
                .Where(entry => !authClientFiles.Contains(entry.relative))
                .Where(entry => AuthClientSpecific().IsMatch(entry.line)))
            .Select(entry => $"{entry.relative}:{entry.number}: {entry.line.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Integration.Tests/AspireFixture.cs"))
            .ShouldContain("test_user_token(App, user)");
    }

    /// <summary>
    /// msal keeps the token its own way (src/auth/accessToken.ts): with its vocabulary stood in, no file but its own, the
    /// API client's headers and the dependencies it adds names MSAL or where the token is kept.
    /// </summary>
    [Theory]
    [MemberData(nameof(MsalGenerations))]
    public void A_project_generated_with_msals_vocabulary_stood_in_names_msal_only_in_its_own_files(string profile, string[] overrides)
    {
        var msal = Repository.Packs.Packs["frontend.auth-client.msal"];
        using var authClient = new VocabularyProbe(msal, AuthClientVocabulary);
        using var project = new GeneratedProject(profile, [authClient.Manifest], overrides);

        var ownFiles = msal.Files.Select(file => file.Target).Concat(AuthClientFiles).Append("web/app/package.json").ToHashSet(StringComparer.Ordinal);
        var named = Code(project)
            .Where(entry => !ownFiles.Contains(entry.relative))
            .Where(entry => MsalSpecific().IsMatch(entry.line))
            .Select(entry => $"{entry.relative}:{entry.number}: {entry.line.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    /// <summary>
    /// The real packs, but the given identity provider's defines nothing in <c>templates.functions</c>: it stands in
    /// for a provider that lacks the vocabulary, so the test needs no option that is still without a pack.
    /// </summary>
    private static Dictionary<string, Core.Packs.PackManifest> WithoutVocabulary(Core.Packs.PackManifest provider)
    {
        var packs = Repository.Packs.Packs.ToDictionary();
        packs[provider.Option] = VocabularyProbe.WithoutFunctions(provider);
        return packs;
    }

    /// <summary>
    /// The code lines of the project's sources, tests and web app with their 1-based line numbers. Comments are prose:
    /// they still describe Keycloak where it is the only provider, and change with the first other provider's output.
    /// </summary>
    private static IEnumerable<(string relative, int number, string line)> Code(GeneratedProject project) =>
        CheckedDirectories
            .Select(directory => Path.Combine(project.ProjectDirectory, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => Source().IsMatch(path))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (relative: Path.GetRelativePath(project.ProjectDirectory, path).Replace('\\', '/'), number: index + 1, line)))
            .Where(entry => !Comment().IsMatch(entry.line));

    [GeneratedRegex(@"Keycloak|keycloak|oidc-client-ts only|realm_access")]
    private static partial Regex IdentityOnly();

    [GeneratedRegex(@"[Kk]eycloak|/realms/|realm_access|resource_access|admin-cli|Username or email|/tenants/")]
    private static partial Regex KeycloakSpecific();

    [GeneratedRegex(@"oidc\.user:|@/auth/accessToken|@/auth/oidcConfig|\baccessToken\(\)")]
    private static partial Regex AuthClientSpecific();

    [GeneratedRegex(@"@azure/msal|app\.access-token|rememberAccessToken|@/auth/accessToken|@/auth/msal|\baccessToken\(\)")]
    private static partial Regex MsalSpecific();

    [GeneratedRegex(@"\.(cs|csproj|ts|tsx|vue|json)$")]
    private static partial Regex Source();

    [GeneratedRegex(@"^\s*(//|/\*|\*|<!--)")]
    private static partial Regex Comment();
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The identity-provider vocabulary (docs/slots.md, M3-13): what depends on the identity provider (where its roles sit
/// in the token, how a test user signs in, its login page, the shape of its ID token) comes from
/// <c>templates.functions</c> of the <c>backend.auth</c> pack, and how the browser holds its access token from those of
/// the <c>frontend.auth-client</c> pack, so generic-oidc, entra-id and msal plug in by defining the same functions
/// instead of editing roles, claims, signalr, ag-ui, the micro-frontends and the base's policy rule.
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

    /// <summary>The files that may name how oidc-client-ts holds the token: its own, and the API client's headers it fills.</summary>
    private static readonly string[] AuthClientFiles =
    [
        "web/app/src/auth/",
        "web/app/src/api/baseApi.ts",
    ];

    /// <summary>
    /// Every user of the vocabulary: roles and claims with their tests, the MCP server, signalr and ag-ui on React, the
    /// Vue sign-in, and the micro-frontends' end-to-end tests.
    /// </summary>
    public static TheoryData<string, string[]> Generations => new()
    {
        { "saas-ai-react", ["backend.authorization=roles,claims", "backend.mcp=client,server", "frontend.realtime=signalr"] },
        { "saas-standard-vue", ["backend.authorization=roles", "frontend.realtime=signalr"] },
        { "industrial-event-driven-mfe", [] },
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
        using var standIns = new StandIns();
        string[] overrides = ["backend.auth=entra-id", "backend.authorization=roles,claims", "frontend.realtime=signalr"];
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, "lean-crud", overrides);

        var generated = Generator.New(Repository.Catalog, packs, "shop", "lean-crud", overrides);

        var errors = string.Join(Environment.NewLine, generated.Errors);
        errors.ShouldContain("role_mapping_source", customMessage: errors);
        errors.ShouldContain("test_user_token", customMessage: errors);
        errors.ShouldContain("accept_query_token", customMessage: errors);
    }

    [Theory]
    [MemberData(nameof(Generations))]
    public void A_project_generated_with_other_vocabularies_names_keycloak_and_oidc_client_ts_only_in_their_own_files(string profile, string[] overrides)
    {
        var keycloak = Repository.Packs.Packs["backend.auth.keycloak"];
        using var identityProvider = new VocabularyProbe(keycloak, IdentityProviderVocabulary);
        using var authClient = new VocabularyProbe(Repository.Packs.Packs["frontend.auth-client.oidc-client-ts"], AuthClientVocabulary);
        using var integration = new VocabularyProbe(Repository.Packs.Packs["backend.test.integration.aspire-fixture"], IntegrationVocabulary);
        using var project = new GeneratedProject(profile, [identityProvider.Manifest, authClient.Manifest, integration.Manifest], overrides);
        project.AddModule("billing");

        var keycloakFiles = keycloak.Files.Select(file => file.Target).Concat(SharedFiles).ToHashSet(StringComparer.Ordinal);
        var named = Code(project)
            .Where(entry => !entry.relative.StartsWith("src/AppHost/", StringComparison.Ordinal) && !keycloakFiles.Contains(entry.relative))
            .Where(entry => KeycloakSpecific().IsMatch(entry.line))
            .Concat(Code(project)
                .Where(entry => !AuthClientFiles.Any(own => entry.relative.StartsWith(own, StringComparison.Ordinal)))
                .Where(entry => AuthClientSpecific().IsMatch(entry.line)))
            .Select(entry => $"{entry.relative}:{entry.number}: {entry.line.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Integration.Tests/AspireFixture.cs"))
            .ShouldContain("test_user_token(App, user)");
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

    [GeneratedRegex(@"[Kk]eycloak|/realms/|realm_access|resource_access|admin-cli|Username or email")]
    private static partial Regex KeycloakSpecific();

    [GeneratedRegex(@"oidc\.user:|@/auth/accessToken|@/auth/oidcConfig|\baccessToken\(\)")]
    private static partial Regex AuthClientSpecific();

    [GeneratedRegex(@"\.(cs|csproj|ts|tsx|vue|json)$")]
    private static partial Regex Source();

    [GeneratedRegex(@"^\s*(//|/\*|\*|<!--)")]
    private static partial Regex Comment();
}

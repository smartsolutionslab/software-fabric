using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// What every identity provider shares (#322): the base owns <c>GET /me</c> and its response type, and a provider
/// supplies only the claim it reads; shared files describe no single provider; and the browser's OIDC client takes its
/// client ID and scope from the provider, which a real Entra ID or generic provider needs configurable. Prose that
/// differs per provider (its local users, its roles, how a claim reaches the token) comes from the provider's vocabulary,
/// so a project with another provider describes that provider, not Keycloak and its realm.
/// </summary>
public sealed partial class SharedIdentityTests
{
    private const string CurrentUser = "src/Host/CurrentUser.cs";

    /// <summary>
    /// Shared files that may name Keycloak whichever provider is chosen: the package versions, where testcontainers
    /// declares <c>Testcontainers.Keycloak</c> once for the providers that run Keycloak (one package, one owner).
    /// </summary>
    private static readonly string[] ProviderSharedFiles = ["Directory.Packages.props"];

    public static TheoryData<string, string> ProvidersAndFrameworks => new()
    {
        { "keycloak", "saas-standard-react" },
        { "keycloak", "saas-standard-vue" },
        { "generic-oidc", "saas-standard-react" },
        { "generic-oidc", "saas-standard-vue" },
        { "entra-id", "saas-standard-react" },
        { "entra-id", "saas-standard-vue" },
    };

    public static TheoryData<string, string> ConfigurableProvidersAndFrameworks => new()
    {
        { "generic-oidc", "saas-standard-react" },
        { "generic-oidc", "saas-standard-vue" },
        { "entra-id", "saas-standard-react" },
        { "entra-id", "saas-standard-vue" },
    };

    /// <summary>
    /// Every user of the provider's prose: roles and claims with their docs, rules and tests, the browser client's docs
    /// and tests on React and Vue, sse and signalr, both integration fixtures, the MCP server and the micro-frontends.
    /// </summary>
    public static TheoryData<string, string, string[]> OtherProvidersAndGenerations
    {
        get
        {
            var data = new TheoryData<string, string, string[]>();
            foreach (var provider in (string[])["generic-oidc", "entra-id"])
            {
                data.Add(provider, "saas-ai-react", ["backend.authorization=roles,claims", "backend.mcp=client,server", "frontend.realtime=signalr"]);
                data.Add(provider, "saas-standard-vue", ["backend.authorization=roles,claims", "frontend.realtime=sse"]);
                data.Add(provider, "saas-standard-react", ["backend.authorization=roles,claims", "frontend.realtime=sse", "backend.test.integration=testcontainers"]);
                data.Add(provider, "industrial-event-driven-mfe", ["backend.authorization=roles"]);
                data.Add(provider, "lean-crud", []);
            }

            return data;
        }
    }

    [Fact]
    public void The_me_endpoint_and_its_response_type_are_the_bases_for_every_identity_provider()
    {
        var owners = Repository.Packs.Packs.Values
            .Where(pack => pack.Files.Any(file => file.Target == CurrentUser))
            .Select(pack => pack.Option)
            .ToList();
        var providersMappingHostEndpoints = Repository.Packs.Packs.Values
            .Where(pack => pack.Dimension == "backend.auth")
            .Where(pack => pack.Slots.Any(contribution => contribution.Slot == "host.endpoints"))
            .Select(pack => pack.Option)
            .ToList();

        owners.ShouldBe(["base.core"]);
        providersMappingHostEndpoints.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ProvidersAndFrameworks))]
    public void The_web_apps_port_comment_in_the_apphost_names_no_single_identity_provider(string provider, string profile)
    {
        var files = Files(profile, $"backend.auth={provider}");

        var webWiring = files["src/AppHost/AppHost.cs"].Split('\n').Single(line => line.Contains("the dev server proxies /api", StringComparison.Ordinal));
        webWiring.ShouldNotContain("realm");
    }

    [Theory]
    [MemberData(nameof(ProvidersAndFrameworks))]
    public void The_integration_test_of_a_signed_in_user_names_no_single_identity_provider(string provider, string profile)
    {
        var files = Files(profile, $"backend.auth={provider}");

        files["tests/Integration.Tests/HostTests.cs"].ShouldContain("public async Task A_token_from_the_identity_provider_identifies_the_user()");
        files["tests/Integration.Tests/HostTests.cs"].ShouldNotContain("A_token_from_the_realm_identifies_the_user");
    }

    [Theory]
    [MemberData(nameof(ConfigurableProvidersAndFrameworks))]
    public void The_browser_client_id_and_scope_come_from_configuration_with_a_provider_that_registers_its_own_client(string provider, string profile)
    {
        var files = Files(profile, $"backend.auth={provider}");

        files["web/app/src/auth/oidcConfig.ts"].ShouldContain("  client_id: import.meta.env.VITE_OIDC_CLIENT_ID ?? 'shop-web',\n  scope: import.meta.env.VITE_OIDC_SCOPE ?? 'openid',\n");
    }

    [Theory]
    [MemberData(nameof(ConfigurableProvidersAndFrameworks))]
    public void The_web_app_declares_the_browser_clients_configuration_with_a_provider_that_registers_its_own_client(string provider, string profile)
    {
        var files = Files(profile, $"backend.auth={provider}");

        files["web/app/src/vite-env.d.ts"].ShouldContain("  readonly VITE_OIDC_AUTHORITY?: string;\n  readonly VITE_OIDC_CLIENT_ID?: string;\n  readonly VITE_OIDC_SCOPE?: string;\n}");
    }

    /// <summary>Characterisation: Keycloak's realm import registers the web client, so its configuration stays as it was.</summary>
    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    public void With_keycloak_the_browser_client_is_the_realms_web_client(string profile)
    {
        var files = Files(profile, "backend.auth=keycloak");

        files["web/app/src/auth/oidcConfig.ts"].ShouldContain("  client_id: 'shop-web',\n");
        files["web/app/src/auth/oidcConfig.ts"].ShouldNotContain("scope");
        files["web/app/src/vite-env.d.ts"].ShouldContain("interface ImportMetaEnv {\n  readonly VITE_OIDC_AUTHORITY?: string;\n}");
    }

    /// <summary>
    /// Gate: with generic-oidc or entra-id, no text of the project, docs and agent rules included, names Keycloak or its
    /// realm, except the provider's own: its files, its AppHost wiring, and what it writes into other files (its slot
    /// contributions, its vocabulary, its ADR and agent rules). Those stand in as empty or as plain calls here, so what
    /// remains is the shared text, which must describe whichever provider is chosen. generic-oidc's local provider is a
    /// Keycloak realm, so its own text may say so; entra-id's is the stand-in.
    /// </summary>
    [Theory]
    [MemberData(nameof(OtherProvidersAndGenerations))]
    public void With_another_identity_provider_no_shared_text_names_keycloak_or_its_realm(string provider, string profile, string[] overrides)
    {
        var real = Repository.Packs.Packs[$"backend.auth.{provider}"];
        using var ownText = new VocabularyProbe(real, VocabularyProbe.Functions(real));
        foreach (var contribution in ownText.Manifest.Slots.Where(contribution => contribution.Slot != VocabularyProbe.FunctionsSlot))
        {
            File.WriteAllText(Path.Combine(ownText.Manifest.Directory, contribution.Template), string.Empty);
        }

        File.WriteAllText(Path.Combine(ownText.Manifest.Directory, ownText.Manifest.Agents.AgentsMd), $"## {provider}\n");
        File.WriteAllText(Path.Combine(ownText.Manifest.Directory, ownText.Manifest.Adr), $"# {provider}\n");
        using var project = new GeneratedProject(profile, [ownText.Manifest], [$"backend.auth={provider}", .. overrides]);
        project.AddModule("billing");

        var own = real.Files.Select(file => file.Target).Concat(ProviderSharedFiles).ToHashSet(StringComparer.Ordinal);
        var named = Directory.EnumerateFiles(project.ProjectDirectory, "*", SearchOption.AllDirectories)
            .Select(path => (path, relative: Path.GetRelativePath(project.ProjectDirectory, path).Replace('\\', '/')))
            .Where(file => !file.relative.StartsWith("src/AppHost/", StringComparison.Ordinal) && !own.Contains(file.relative))
            .SelectMany(file => File.ReadLines(file.path).Select((line, index) => (file.relative, number: index + 1, line)))
            .Where(entry => KeycloakOrRealm().IsMatch(entry.line))
            .Select(entry => $"{entry.relative}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex("keycloak|realm", RegexOptions.IgnoreCase)]
    private static partial Regex KeycloakOrRealm();
}

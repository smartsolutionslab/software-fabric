using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// What every identity provider shares (#322): the base owns <c>GET /me</c> and its response type, and a provider
/// supplies only the claim it reads; shared files describe no single provider; and the browser's OIDC client takes its
/// client ID and scope from the provider, which a real Entra ID or generic provider needs configurable.
/// </summary>
public sealed class SharedIdentityTests
{
    private const string CurrentUser = "src/Host/CurrentUser.cs";

    public static TheoryData<string, string> ProvidersAndFrameworks => new()
    {
        { "keycloak", "saas-standard-react" },
        { "keycloak", "saas-standard-vue" },
        { "generic-oidc", "saas-standard-react" },
        { "generic-oidc", "saas-standard-vue" },
        { "entra-id", "saas-standard-react" },
        { "entra-id", "saas-standard-vue" },
    };

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

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

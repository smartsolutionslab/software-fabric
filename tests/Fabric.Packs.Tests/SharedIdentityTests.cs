namespace Fabric.Packs.Tests;

/// <summary>
/// What every identity provider shares (#322): the base owns <c>GET /me</c> and its response type, and a provider
/// supplies only the claim it reads; shared files describe no single provider; and the browser's OIDC client takes its
/// client ID and scope from the provider, which a real Entra ID or generic provider needs configurable.
/// </summary>
public sealed class SharedIdentityTests
{
    private const string CurrentUser = "src/Host/CurrentUser.cs";

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
}

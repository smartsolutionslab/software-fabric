using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.authorization: [claims]</c> (ADR-0009): access by claim, profile information the identity provider puts in
/// the token. Alone or together with roles, where one policy may require a role and a claim.
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
}

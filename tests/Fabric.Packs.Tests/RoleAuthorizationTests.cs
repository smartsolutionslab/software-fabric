using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.authorization: [roles]</c> (ADR-0009): access by the identity provider's roles in the token. Roles need
/// someone to sign in, so the dimension exists only while the project authenticates.
/// </summary>
public sealed class RoleAuthorizationTests
{
    private const string Profile = "lean-crud";
    private const string Roles = "backend.authorization=roles";

    [Fact]
    public void A_project_with_roles_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Roles]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Roles_without_authentication_are_refused_saying_why()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, ["backend.auth=none", Roles]);

        generated.Errors.ShouldContain("backend.authorization is set to roles, but it only applies when backend.auth != none, and backend.auth is none.");
    }
}

using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.forms.react-hook-form takes @hookform/resolvers 7 (#592). Its zod resolver is unchanged from 6.0.2; its
/// valibot resolver now keeps object-level issues, which have no path, on the empty key instead of dropping them.
/// </summary>
public sealed class ReactHookFormResolversTests
{
    public static TheoryData<string, string> ReactValidators => new()
    {
        { "saas-standard-react", "frontend.validation=zod" },
        { "saas-standard-react", "frontend.validation=valibot" },
    };

    [Theory]
    [MemberData(nameof(ReactValidators))]
    public void React_hook_form_resolves_with_hookform_resolvers_7(string profile, string validation)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, [validation]);
        generated.Errors.ShouldBeEmpty();

        generated.Files.Single(file => file.Path == "web/app/package.json").Content.ShouldContain("\"@hookform/resolvers\": \"7.0.5\"");
    }
}

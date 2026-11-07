using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>e2e.after-tests</c>, a role slot of <c>frontend.framework</c> (ADR-0008): commands <c>.github/scripts/e2e.sh</c>
/// runs once the end-to-end tests have passed, while the application it started still runs, so a pack can test the
/// running application with a tool of its own. Without a contribution the script ends where it always did.
/// </summary>
public sealed class EndToEndAfterTestsSlotTests
{
    private const string Script = ".github/scripts/e2e.sh";
    private const string EndToEndTests = "(cd web/app && pnpm e2e) || fail \"The end-to-end tests failed.\"";

    public static TheoryData<string> Profiles { get; } = ["saas-standard-react", "saas-standard-vue"];

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_command_another_pack_contributes_runs_after_the_end_to_end_tests_while_the_application_runs(string profile)
    {
        using var probe = new ProbePack("frontend.pwa.yes", new Dictionary<string, string>
        {
            ["e2e.after-tests"] = "(cd web/app && pnpm probe) || fail \"The probe failed.\"",
        });
        var packs = Repository.Packs.Packs.ToDictionary();
        packs[probe.Manifest.Option] = probe.Manifest;

        var generated = Generator.New(Repository.Catalog, packs, "shop", profile, ["frontend.pwa=yes"]);
        generated.Errors.ShouldBeEmpty();

        var script = generated.Files.Single(file => file.Path == Script).Content;
        script.ShouldEndWith($"{EndToEndTests}\n(cd web/app && pnpm probe) || fail \"The probe failed.\"\n");
        script.IndexOf("trap stop EXIT", StringComparison.Ordinal).ShouldBeLessThan(script.IndexOf("pnpm probe", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Without_a_contribution_the_script_ends_with_the_end_to_end_tests(string profile)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
        generated.Errors.ShouldBeEmpty();

        generated.Files.Single(file => file.Path == Script).Content.ShouldEndWith($"{EndToEndTests}\n");
    }
}

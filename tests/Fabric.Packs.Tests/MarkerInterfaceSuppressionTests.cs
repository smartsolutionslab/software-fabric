using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Marker interfaces silence no rule (#363). The mediator, domain-event and value-object markers once carried
/// <c>#pragma warning disable CA1040</c>, against the generated rule never to silence a rule with a pragma. CA1040 is
/// not part of the <c>latest-recommended</c> analysis level a generated project builds with, so the pragmas
/// suppressed nothing and are gone; a template that adds one back fails here.
/// </summary>
public sealed class MarkerInterfaceSuppressionTests
{
    public static TheoryData<string, string[]> Projects
    {
        get
        {
            TheoryData<string, string[]> projects = [];
            foreach (var profile in Repository.Catalog.Profiles.Keys)
            {
                projects.Add(profile, []);
            }

            projects.Add("saas-standard-react", ["backend.mediator=wolverine"]);
            return projects;
        }
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void No_generated_file_silences_CA1040_with_a_pragma(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();

        var suppressions = generated.Files
            .Where(file => file.Content.Contains("#pragma warning disable CA1040", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToList();

        suppressions.ShouldBeEmpty();
    }
}

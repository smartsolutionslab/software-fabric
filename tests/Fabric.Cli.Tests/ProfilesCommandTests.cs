namespace Fabric.Cli.Tests;

public class ProfilesCommandTests
{
    [Fact]
    public void Lists_every_profile_one_per_line_in_catalog_order()
    {
        var run = CliRun.Of("profiles", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(0);
        run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(["saas-standard-react", "saas-standard-vue", "industrial-event-driven-mfe", "lean-crud"]);
    }

    [Fact]
    public void Lists_the_fixture_profiles()
    {
        CliRun.Of("profiles", "--catalog", Paths.FixtureCatalog).Output
            .ShouldBe($"minimal{Environment.NewLine}everything{Environment.NewLine}");
    }
}

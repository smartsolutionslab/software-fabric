namespace Fabric.Cli.Tests;

public class ValidateCommandTests
{
    [Fact]
    public void A_valid_profile_exits_zero()
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-react", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(0);
        run.Output.ShouldStartWith("valid: profile saas-standard-react");
        run.Error.ShouldBeEmpty();
    }

    [Fact]
    public void Uses_the_catalog_shipped_next_to_the_tool_by_default()
    {
        CliRun.Of("validate", "--profile", "lean-crud").ExitCode.ShouldBe(0);
    }

    [Fact]
    public void A_rule_violation_exits_one_and_prints_the_sentence()
    {
        var run = CliRun.Of("validate", "--profile", "saas-standard-vue", "--set", "frontend.state=redux-toolkit", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: frontend.state=redux-toolkit requires frontend.framework == react, but frontend.framework is vue.");
    }

    [Fact]
    public void Several_overrides_are_applied()
    {
        var run = CliRun.Of(
            "validate",
            "--profile", "lean-crud",
            "--set", "backend.bus.transport=kafka",
            "--set", "backend.topology=services",
            "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void An_unknown_dimension_exits_one()
    {
        var run = CliRun.Of("validate", "--set", "frontend.colour=red", "--catalog", Paths.RepositoryCatalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("error: unknown dimension 'frontend.colour'.");
    }

    [Fact]
    public void A_broken_catalog_exits_one_with_its_location()
    {
        var catalog = Path.Combine(Directory.CreateTempSubdirectory("fabric-cli-").FullName, "catalog.yaml");
        File.WriteAllText(catalog, "version: 1\ndimensions:\n  a.b: { scope: everywhere, options: { x: { tier: supported } } }\n");

        var run = CliRun.Of("validate", "--catalog", catalog);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain($"error: {catalog}:3:");
    }
}

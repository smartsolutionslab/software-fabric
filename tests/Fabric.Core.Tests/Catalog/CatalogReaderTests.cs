using Fabric.Core.Catalog;

namespace Fabric.Core.Tests.Catalog;

public class CatalogReaderTests
{
    private static CatalogModel ReadRepositoryCatalog() =>
        CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

    private static CatalogModel Read(string yaml) =>
        CatalogReader.Read(new StringReader(yaml), "test.yaml");

    [Fact]
    public void Reads_every_dimension_option_and_profile_of_the_repository_catalog()
    {
        var catalog = ReadRepositoryCatalog();

        catalog.Version.ShouldBe(1);
        catalog.Dimensions.Count.ShouldBe(41);
        catalog.Dimensions.Values.Sum(dimension => dimension.Options.Count).ShouldBe(115);
        catalog.Profiles.Keys.ShouldBe(
            ["saas-standard-react", "saas-standard-vue", "industrial-event-driven-mfe", "lean-crud"],
            ignoreOrder: false);
    }

    [Fact]
    public void Keeps_dimensions_in_file_order()
    {
        var catalog = ReadRepositoryCatalog();

        catalog.Dimensions.Keys.First().ShouldBe("backend.database");
        catalog.Dimensions.Keys.Last().ShouldBe("frontend.test.extras");
    }

    [Fact]
    public void Reads_scope_multi_active_when_and_default()
    {
        var catalog = ReadRepositoryCatalog();

        var busLibrary = catalog.Dimensions["backend.bus.library"];
        busLibrary.Scope.ShouldBe(DimensionScope.Project);
        busLibrary.Multi.ShouldBeFalse();
        busLibrary.ActiveWhen.ShouldBe("backend.bus.transport != none");
        busLibrary.Default.ShouldBe(["wolverine"]);

        var protocols = catalog.Dimensions["backend.api-protocols"];
        protocols.Multi.ShouldBeTrue();
        protocols.Default.ShouldBe(["rest"]);

        catalog.Dimensions["module.style"].Scope.ShouldBe(DimensionScope.Module);
        catalog.Dimensions["backend.test.extras"].Default.ShouldBe([]);
    }

    [Fact]
    public void Reads_option_tier_requires_and_conflicts()
    {
        var catalog = ReadRepositoryCatalog();
        var uiLibraries = catalog.Dimensions["frontend.ui-library"].Options;

        uiLibraries["shadcn"].Tier.ShouldBe(SupportTier.Supported);
        uiLibraries["shadcn"].Requires.ShouldBe(["frontend.framework == react", "frontend.styling == tailwind"]);
        uiLibraries["mui"].Conflicts.ShouldBe(["frontend.styling == tailwind"]);
        catalog.Dimensions["module.event-store"].Options["kurrentdb"].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Fact]
    public void Reads_yes_and_no_as_option_names_not_booleans()
    {
        var catalog = ReadRepositoryCatalog();

        catalog.Dimensions["frontend.pwa"].Options.Keys.ShouldBe(["no", "yes"]);
        catalog.Dimensions["frontend.pwa"].Default.ShouldBe(["no"]);
    }

    [Fact]
    public void Reads_profile_settings_and_extends()
    {
        var catalog = ReadRepositoryCatalog();
        var vue = catalog.Profiles["saas-standard-vue"];

        vue.Extends.ShouldBe("saas-standard-react");
        vue.Settings["frontend.framework"].ShouldBe(["vue"]);
        catalog.Profiles["saas-standard-react"].Extends.ShouldBeNull();
    }

    [Fact]
    public void Rejects_an_unknown_field_on_a_dimension_with_its_line()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
                colour: red
                options:
                  x: { tier: supported }
            """));

        exception.Message.ShouldContain("test.yaml:5");
        exception.Message.ShouldContain("'colour'");
    }

    [Fact]
    public void Rejects_an_unknown_field_on_an_option()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
                options:
                  x: { tier: supported, needs: [c.d == e] }
            """));

        exception.Message.ShouldContain("'needs'");
    }

    [Fact]
    public void Rejects_a_dimension_without_options()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
            """));

        exception.Message.ShouldContain("'options'");
    }

    [Theory]
    [InlineData("scope: global", "scope")]
    [InlineData("scope: project\n    multi: maybe", "multi")]
    public void Rejects_an_invalid_dimension_value(string fields, string field)
    {
        var exception = Should.Throw<CatalogException>(() => Read($"""
            version: 1
            dimensions:
              a.b:
                {fields}
                options:
                  x: {"{"} tier: supported {"}"}
            """));

        exception.Message.ShouldContain($"'{field}'");
    }

    [Fact]
    public void Rejects_an_unknown_tier()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
                options:
                  x: { tier: beta }
            """));

        exception.Message.ShouldContain("'beta'");
    }

    [Fact]
    public void Rejects_a_missing_version()
    {
        Should.Throw<CatalogException>(() => Read("dimensions: {}"))
            .Message.ShouldContain("'version'");
    }
}

using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Tests.Choices;

public class ChoiceResolverTests
{
    private static readonly CatalogModel RepositoryCatalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

    private static ResolvedChoices ResolveSuccessfully(string? profile, params string[] overrides)
    {
        var resolution = ChoiceResolver.Resolve(RepositoryCatalog, profile, overrides);
        resolution.Errors.ShouldBeEmpty();
        return resolution.Choices.ShouldNotBeNull();
    }

    private static string SingleError(CatalogModel catalog, string? profile, params string[] overrides)
    {
        var resolution = ChoiceResolver.Resolve(catalog, profile, overrides);
        resolution.Choices.ShouldBeNull();
        return resolution.Errors.ShouldHaveSingleItem();
    }

    private static CatalogModel Catalog(string yaml) => CatalogReader.Read(new StringReader(yaml), "test.yaml");

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("lean-crud")]
    public void Every_profile_resolves(string profile)
    {
        ResolveSuccessfully(profile).Project.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_profile_value_wins_over_the_default()
    {
        ResolveSuccessfully("saas-standard-react").Project["backend.tenancy"].ShouldBe(["column"]);
    }

    [Fact]
    public void An_unset_active_dimension_takes_its_default()
    {
        var choices = ResolveSuccessfully("saas-standard-react");

        choices.Project["backend.database"].ShouldBe(["postgres"]);
        choices.Project["backend.bus.library"].ShouldBe(["wolverine"]);
        choices.ModuleDefaults["module.ddd-depth"].ShouldBe(["light"]);
    }

    [Fact]
    public void An_inactive_dimension_is_left_out()
    {
        var choices = ResolveSuccessfully("lean-crud");

        choices.Project.ShouldNotContainKey("backend.bus.library");
        choices.Project.ShouldNotContainKey("frontend.mfe-technique");
        choices.ModuleDefaults.ShouldNotContainKey("module.ddd-depth");
        choices.ModuleDefaults.ShouldNotContainKey("module.event-store");
    }

    [Fact]
    public void Project_and_module_dimensions_are_kept_apart()
    {
        var choices = ResolveSuccessfully("lean-crud");

        choices.Project.Keys.ShouldAllBe(dimension => !dimension.StartsWith("module.", StringComparison.Ordinal));
        choices.ModuleDefaults.Keys.ShouldBe(["module.style", "module.cqrs", "module.communication"]);
    }

    [Fact]
    public void A_child_profile_inherits_and_overrides_its_parent()
    {
        var choices = ResolveSuccessfully("saas-standard-vue");

        choices.Project["frontend.framework"].ShouldBe(["vue"]);
        choices.Project["backend.tenancy"].ShouldBe(["column"]);
        choices.Project["backend.bus.transport"].ShouldBe(["rabbitmq"]);
    }

    [Fact]
    public void Overrides_win_over_the_profile_and_activate_dependent_dimensions()
    {
        var choices = ResolveSuccessfully("lean-crud", "backend.bus.transport=kafka");

        choices.Project["backend.bus.transport"].ShouldBe(["kafka"]);
        choices.Project["backend.bus.library"].ShouldBe(["wolverine"]);
    }

    [Fact]
    public void A_multi_dimension_override_takes_a_comma_separated_set()
    {
        ResolveSuccessfully(null, "backend.api-protocols=rest,grpc").Project["backend.api-protocols"].ShouldBe(["rest", "grpc"]);
    }

    [Fact]
    public void Without_a_profile_every_active_dimension_takes_its_default()
    {
        var choices = ResolveSuccessfully(null);

        choices.Project["frontend.framework"].ShouldBe(["react"]);
        choices.Project["backend.test.extras"].ShouldBeEmpty();
        choices.Project.ShouldNotContainKey("backend.bus.library");
    }

    [Fact]
    public void Values_follow_catalog_order()
    {
        var choices = ResolveSuccessfully("saas-standard-vue");

        choices.Project.Keys.First().ShouldBe("backend.database");
        choices.Project.Keys.Last().ShouldBe("frontend.test.extras");
    }

    [Fact]
    public void Rejects_an_unknown_profile()
    {
        SingleError(RepositoryCatalog, "enterprise").ShouldBe("unknown profile 'enterprise'.");
    }

    [Theory]
    [InlineData("frontend.colour=red", "unknown dimension 'frontend.colour'.")]
    [InlineData("frontend.framework=svelte", "'svelte' is not an option of frontend.framework; options: react, vue.")]
    [InlineData("frontend.framework=react,vue", "frontend.framework takes one option, got 2: react, vue.")]
    [InlineData("frontend.framework", "override 'frontend.framework' must look like <dimension>=<option>.")]
    public void Rejects_an_invalid_override(string setting, string error)
    {
        SingleError(RepositoryCatalog, null, setting).ShouldBe(error);
    }

    [Fact]
    public void Rejects_setting_an_inactive_dimension_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.bus.library=masstransit").ShouldBe(
            "backend.bus.library is set to masstransit, but it only applies when backend.bus.transport != none, and backend.bus.transport is none.");
    }

    [Fact]
    public void Rejects_a_profile_that_extends_itself()
    {
        var catalog = Catalog("""
            version: 1
            profiles:
              a: { extends: b }
              b: { extends: a }
            dimensions:
              x.y: { scope: project, default: o, options: { o: { tier: supported } } }
            """);

        SingleError(catalog, "a").ShouldBe("profile 'a' extends itself: a -> b -> a.");
    }

    [Fact]
    public void Rejects_a_profile_that_extends_an_unknown_profile()
    {
        var catalog = Catalog("""
            version: 1
            profiles:
              a: { extends: missing }
            dimensions:
              x.y: { scope: project, default: o, options: { o: { tier: supported } } }
            """);

        SingleError(catalog, "a").ShouldBe("profile 'a' extends unknown profile 'missing'.");
    }

    [Fact]
    public void Rejects_an_active_dimension_without_value_or_default()
    {
        var catalog = Catalog("""
            version: 1
            dimensions:
              x.y: { scope: project, options: { o: { tier: supported } } }
            """);

        SingleError(catalog, null).ShouldBe("x.y has no value and no default; set it with x.y=<option>.");
    }

    [Fact]
    public void A_default_is_dropped_when_another_default_deactivates_its_dimension()
    {
        var catalog = Catalog("""
            version: 1
            dimensions:
              b.b:
                scope: project
                active-when: "a.a != off"
                default: y
                options: { y: { tier: supported } }
              a.a:
                scope: project
                default: "off"
                options: { "on": { tier: supported }, "off": { tier: supported } }
            """);

        var resolution = ChoiceResolver.Resolve(catalog, null, []);

        resolution.Errors.ShouldBeEmpty();
        resolution.Choices.ShouldNotBeNull().Project.Keys.ShouldBe(["a.a"]);
    }
}

using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Tests.Choices;

public class ModuleResolutionTests
{
    private static readonly CatalogModel RepositoryCatalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

    private static ProjectChoices Project(string profile)
    {
        var resolved = ChoiceResolver.Resolve(RepositoryCatalog, profile, []).Choices.ShouldNotBeNull();
        return new ProjectChoices(resolved.Project, resolved.ModuleDefaults, new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ResolveSuccessfully(string profile, params string[] overrides)
    {
        var resolution = ChoiceResolver.ResolveModule(RepositoryCatalog, Project(profile), overrides);
        resolution.Errors.ShouldBeEmpty();
        return resolution.Values.ShouldNotBeNull();
    }

    [Fact]
    public void Without_overrides_a_module_takes_the_project_module_defaults()
    {
        var values = ResolveSuccessfully("industrial-event-driven-mfe");

        values["module.style"].ShouldBe(["event-sourced"]);
        values["module.cqrs"].ShouldBe(["physical"]);
        values["module.event-store"].ShouldBe(["marten"]);
    }

    [Fact]
    public void An_override_that_activates_a_dimension_brings_in_its_catalog_default()
    {
        var values = ResolveSuccessfully("saas-standard-react", "module.style=event-sourced");

        values["module.event-store"].ShouldBe(["marten"]);
        values["module.ddd-depth"].ShouldBe(["light"]);
    }

    [Fact]
    public void An_override_that_deactivates_a_dimension_drops_its_default()
    {
        var values = ResolveSuccessfully("saas-standard-react", "module.style=crud");

        values.ShouldNotContainKey("module.ddd-depth");
        values.Keys.ShouldBe(["module.style", "module.cqrs", "module.communication"]);
    }

    [Fact]
    public void A_project_scoped_override_is_rejected()
    {
        ChoiceResolver.ResolveModule(RepositoryCatalog, Project("saas-standard-react"), ["backend.database=sqlserver"])
            .Errors.ShouldBe(["backend.database is project-scoped; it was fixed when the project was generated."]);
    }

    [Fact]
    public void Setting_an_inactive_module_dimension_is_rejected()
    {
        ChoiceResolver.ResolveModule(RepositoryCatalog, Project("lean-crud"), ["module.event-store=marten"])
            .Errors.ShouldBe(["module.event-store is set to marten, but it only applies when module.style == event-sourced, and module.style is crud."]);
    }
}

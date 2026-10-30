using Fabric.Core.Generation;

namespace Fabric.Core.Tests.Generation;

public class AddModuleTests
{
    private static FabricRecord Generated(string profile)
    {
        var generated = Generator.New(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, "shop", profile, []);
        generated.Errors.ShouldBeEmpty();
        var fabricYaml = generated.Files.Single(file => file.Path == FabricFile.FileName).Content;
        return FabricFile.Read(new StringReader(fabricYaml), FabricFile.FileName);
    }

    private static ModuleAddition Add(FabricRecord project, string name, params string[] overrides) =>
        Generator.AddModule(Fixtures.MinimalCatalog, Fixtures.MinimalPacks, project, name, overrides);

    [Fact]
    public void Creates_the_module_files_and_records_the_module()
    {
        var addition = Add(Generated("minimal"), "billing");

        addition.Errors.ShouldBeEmpty();
        addition.Created.ShouldHaveSingleItem().ShouldBe(new("src/billing/README.md", "# billing (ddd)\n"));
        addition.FabricYaml.ShouldNotBeNull().ShouldEndWith("modules:\n  billing:\n    module.style: ddd\n");
    }

    [Fact]
    public void Keeps_existing_modules_and_appends_the_new_one()
    {
        var first = Add(Generated("minimal"), "billing");
        var withBilling = FabricFile.Read(new StringReader(first.FabricYaml!), FabricFile.FileName);

        var second = Add(withBilling, "audit");

        second.Errors.ShouldBeEmpty();
        second.Created.Select(file => file.Path).ShouldBe(["src/audit/README.md"]);
        second.FabricYaml.ShouldNotBeNull().ShouldEndWith("modules:\n  billing:\n    module.style: ddd\n  audit:\n    module.style: ddd\n");
    }

    [Theory]
    [InlineData("Billing")]
    [InlineData("1st")]
    [InlineData("billing_v2")]
    [InlineData("")]
    public void Rejects_a_name_that_is_not_lower_kebab_case(string name)
    {
        Add(Generated("minimal"), name).Errors.ShouldBe([$"module name '{name}' must be lower-case letters, digits and hyphens, starting with a letter."]);
    }

    [Fact]
    public void Rejects_a_module_that_already_exists()
    {
        var withBilling = FabricFile.Read(new StringReader(Add(Generated("minimal"), "billing").FabricYaml!), FabricFile.FileName);

        Add(withBilling, "billing").Errors.ShouldBe(["module 'billing' already exists."]);
    }

    [Fact]
    public void Rejects_a_project_scoped_override()
    {
        Add(Generated("minimal"), "billing", "backend.database=sqlserver")
            .Errors.ShouldBe(["backend.database is project-scoped; it was fixed when the project was generated."]);
    }

    [Fact]
    public void Rejects_an_option_whose_pack_would_change_existing_files()
    {
        Add(Generated("minimal"), "billing", "module.style=crud").Errors.ShouldBe([
            "module.style=crud is new to this project and its pack contributes to project-wide files (slot readme.sections); " +
            "add-module only creates files, so choose it when generating the project or add it by hand.",
        ]);
    }
}

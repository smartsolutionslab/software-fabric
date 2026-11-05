using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Generation;

/// <summary>
/// A pack may bring a module with pinned choices (ADR-0005): <c>fabric new</c> adds it as <c>add-module</c> would,
/// and <c>add-module</c> refuses the name.
/// </summary>
public sealed class PackModuleTests : IDisposable
{
    private readonly string packsRoot = Directory.CreateTempSubdirectory("fabric-pack-modules-").FullName;

    public PackModuleTests()
    {
        var source = Path.Combine(Fixtures.MinimalRoot, "packs");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(packsRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    public void Dispose() => Directory.Delete(packsRoot, recursive: true);

    /// <summary>Prepends YAML to a fixture pack's manifest; <paramref name="pack"/> is its directory path, dot-separated.</summary>
    private PackModuleTests Declare(string pack, string yaml)
    {
        var manifest = Path.Combine([packsRoot, .. pack.Split('.'), "pack.yaml"]);
        File.WriteAllText(manifest, yaml + "\n" + File.ReadAllText(manifest));
        return this;
    }

    private IReadOnlyDictionary<string, PackManifest> Packs()
    {
        var loaded = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog);
        loaded.Errors.ShouldBeEmpty();
        return loaded.Packs;
    }

    private GenerationResult New(string profile) => Generator.New(Fixtures.MinimalCatalog, Packs(), "shop", profile, []);

    private static string FabricYaml(GenerationResult generated) => generated.Files.Single(file => file.Path == FabricFile.FileName).Content;

    [Fact]
    public void A_pack_declared_module_is_generated_with_its_pinned_choices()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: { module.style: crud } }");

        var generated = New("minimal");

        generated.Errors.ShouldBeEmpty();
        generated.Files.Single(file => file.Path == "src/reports/README.md").Content.ShouldBe("# reports (crud)\n");
        FabricYaml(generated).ShouldEndWith("modules:\n  reports:\n    module.style: crud\n");
    }

    [Fact]
    public void A_choice_the_declaration_leaves_open_comes_from_the_module_defaults()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: {} }");

        var generated = New("minimal");

        generated.Errors.ShouldBeEmpty();
        generated.Files.Single(file => file.Path == "src/reports/README.md").Content.ShouldBe("# reports (ddd)\n");
        FabricYaml(generated).ShouldEndWith("modules:\n  reports:\n    module.style: ddd\n");
    }

    [Fact]
    public void Declared_modules_are_recorded_in_name_order()
    {
        Declare("backend.api-protocols.rest", "modules: { sales: {}, audit: { module.style: crud } }");

        FabricYaml(New("minimal")).ShouldEndWith("modules:\n  audit:\n    module.style: crud\n  sales:\n    module.style: ddd\n");
    }

    [Fact]
    public void The_declaring_pack_contributes_files_for_its_module_only()
    {
        var rest = Path.Combine(packsRoot, "backend", "api-protocols", "rest");
        File.WriteAllText(Path.Combine(rest, "templates", "report.sbn"), "report of {{ module.name }}");
        var manifest = Path.Combine(rest, "pack.yaml");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("files: []\n", """
            files:
              - { template: templates/report.sbn, target: "src/{{ module.name }}/Report.txt", per-module: true, when: 'module.name == "reports"' }

            """, StringComparison.Ordinal));
        Declare("backend.api-protocols.rest", "modules: { reports: { module.style: crud }, sales: {} }");

        var generated = New("minimal");

        generated.Errors.ShouldBeEmpty();
        generated.Files.Single(file => file.Path == "src/reports/Report.txt").Content.ShouldBe("report of reports");
        generated.Files.Select(file => file.Path).ShouldNotContain("src/sales/Report.txt");
    }

    [Fact]
    public void Add_module_refuses_a_name_a_pack_brings()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: { module.style: crud } }");
        var packs = Packs();
        var generated = Generator.New(Fixtures.MinimalCatalog, packs, "shop", "minimal", []);
        var project = FabricFile.Read(new StringReader(FabricYaml(generated)), FabricFile.FileName);

        Generator.AddModule(Fixtures.MinimalCatalog, packs, project, "reports", []).Errors.ShouldBe([
            "module 'reports' is brought by backend.api-protocols.rest; fabric new adds it with that pack's pinned choices, so add-module cannot add it.",
        ]);
    }

    [Fact]
    public void Add_module_refuses_a_name_a_pack_brings_even_when_fabric_yaml_lacks_it()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: {} }");
        var project = new FabricRecord(
            "shop",
            "minimal",
            new ProjectChoices(
                new Dictionary<string, IReadOnlyList<string>> { ["backend.database"] = ["postgres"], ["backend.api-protocols"] = ["rest"] },
                new Dictionary<string, IReadOnlyList<string>> { ["module.style"] = ["ddd"] },
                new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>()));

        Generator.AddModule(Fixtures.MinimalCatalog, Packs(), project, "reports", []).Errors.ShouldBe([
            "module 'reports' is brought by backend.api-protocols.rest; fabric new adds it with that pack's pinned choices, so add-module cannot add it.",
        ]);
    }

    [Fact]
    public void Add_module_still_adds_other_modules_beside_a_declared_one()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: {} }");
        var packs = Packs();
        var generated = Generator.New(Fixtures.MinimalCatalog, packs, "shop", "minimal", []);
        var project = FabricFile.Read(new StringReader(FabricYaml(generated)), FabricFile.FileName);

        var addition = Generator.AddModule(Fixtures.MinimalCatalog, packs, project, "billing", []);

        addition.Errors.ShouldBeEmpty();
        addition.Created.Select(file => file.Path).ShouldBe(["src/billing/README.md"]);
        addition.FabricYaml.ShouldNotBeNull().ShouldEndWith("modules:\n  reports:\n    module.style: ddd\n  billing:\n    module.style: ddd\n");
    }

    [Fact]
    public void Conflicting_declarations_fail_naming_both_packs()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: { module.style: crud } }");
        Declare("backend.api-protocols.grpc", "modules: { reports: { module.style: ddd } }");

        New("everything").Errors.ShouldBe([
            "module 'reports': backend.api-protocols.grpc pins module.style=ddd, but backend.api-protocols.rest pins module.style=crud.",
        ]);
    }

    [Fact]
    public void Agreeing_declarations_bring_one_module()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: { module.style: crud } }");
        Declare("backend.api-protocols.grpc", "modules: { reports: { module.style: crud } }");

        var generated = New("everything");

        generated.Errors.ShouldBeEmpty();
        FabricYaml(generated).ShouldEndWith("modules:\n  reports:\n    module.style: crud\n");
    }

    [Fact]
    public void A_pinned_option_the_catalog_does_not_have_is_an_error_naming_the_pack()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: { module.style: hexagonal } }");

        New("minimal").Errors.ShouldBe([
            "backend.api-protocols.rest: module 'reports': 'hexagonal' is not an option of module.style; options: crud, ddd.",
        ]);
    }

    [Fact]
    public void A_pinned_project_scoped_choice_is_an_error_naming_the_pack()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: { backend.database: sqlserver } }");

        New("minimal").Errors.ShouldBe([
            "backend.api-protocols.rest: module 'reports': backend.database is project-scoped; it was fixed when the project was generated.",
        ]);
    }

    [Fact]
    public void A_pack_that_is_not_selected_brings_no_module()
    {
        Declare("backend.api-protocols.grpc", "modules: { reports: {} }");

        var generated = New("minimal");

        generated.Errors.ShouldBeEmpty();
        FabricYaml(generated).ShouldEndWith("modules: {}\n");
    }

    [Fact]
    public void A_module_scoped_pack_cannot_bring_a_module()
    {
        Declare("module.style.crud", "modules: { reports: {} }");

        PackLoader.Load(packsRoot, Fixtures.MinimalCatalog).Errors.ShouldBe([
            "module/style/crud/pack.yaml: brings modules, but module.style is module-scoped; only a base or project-scoped pack may bring a module.",
        ]);
    }

    [Fact]
    public void A_declared_module_name_that_is_not_lower_kebab_case_is_rejected_by_the_schema()
    {
        Declare("backend.api-protocols.rest", "modules: { Reports: {} }");

        var errors = PackLoader.Load(packsRoot, Fixtures.MinimalCatalog).Errors;

        errors.ShouldNotBeEmpty();
        errors.ShouldAllBe(error => error.StartsWith("backend/api-protocols/rest/pack.yaml: /modules", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pin_may_be_written_as_a_list()
    {
        Declare("backend.api-protocols.rest", "modules: { reports: { module.style: [crud] } }");

        var generated = New("minimal");

        generated.Errors.ShouldBeEmpty();
        FabricYaml(generated).ShouldEndWith("modules:\n  reports:\n    module.style: crud\n");
    }
}

using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #844, #853: every file <c>fabric add-module</c> writes under <c>web/</c> (the sample hook <c>use&lt;Module&gt;Samples.ts</c>
/// and its test, the page, its data, routes, form and tests, the e2e spec) is printed as Prettier prints it at the web
/// app's own <c>printWidth</c> for a module name of any length up to 63, so <c>pnpm format:check</c> passes straight after
/// <c>fabric add-module</c>. The profiles' modules have short names, so their own format checks cannot see a line that
/// only breaks, or only joins, for a longer or a shorter one.
/// </summary>
public sealed class SampleHookFormattingTests
{
    /// <summary>
    /// Every prefix of these names that does not end in a hyphen is a module: from one letter to 63, the most a name can
    /// have and still be its module's PostgreSQL schema whole (PostgreSQL cuts an identifier at 63 bytes). One has
    /// hyphens and one has none, so the route (<c>module.name</c>) and the types (<c>module.namespace</c>) reach their
    /// longest in different names.
    /// </summary>
    private static readonly string[] LongestNames =
    [
        "warehouse-inventory-replenishment-planning-and-order-fulfilment",
        "warehouseinventoryreplenishmentplanningandorderfulfilmentreturn",
    ];

    /// <summary>Every branch of every api-client pack's hook: the client, and for Vue whether Pinia or TanStack Query holds server data. The framework decides the page, routes, form and tests the module gets beside it.</summary>
    public static TheoryData<string, string[]> Clients => new()
    {
        { "saas-standard-react", [] },
        { "lean-crud", ["frontend.api-client=orval"] },
        { "saas-standard-vue", ["frontend.api-client=orval"] },
        { "saas-standard-vue", ["frontend.api-client=orval", "frontend.state=pinia"] },
        { "lean-crud", ["frontend.api-client=openapi-fetch"] },
        { "saas-standard-vue", ["frontend.api-client=openapi-fetch"] },
        { "saas-standard-vue", ["frontend.api-client=openapi-fetch", "frontend.state=pinia"] },
    };

    [Theory]
    [MemberData(nameof(Clients))]
    public void Every_web_file_add_module_writes_is_prettier_clean_for_every_module_name_up_to_63_characters(string profile, string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        var record = FabricFile.Read(new StringReader(File.ReadAllText(Path.Combine(project.ProjectDirectory, FabricFile.FileName))), FabricFile.FileName);
        var app = Path.Combine(project.ProjectDirectory, "web", "app");
        List<string> files = [];
        foreach (var name in ModuleNames())
        {
            var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, name, []);
            addition.Errors.ShouldBeEmpty();
            foreach (var file in addition.Created.Where(file => file.Path.StartsWith("web/", StringComparison.Ordinal)))
            {
                project.Write(file.Path, file.Content);
                files.Add(Path.GetRelativePath(app, Path.Combine(project.ProjectDirectory, file.Path)));
            }
        }

        files.ShouldContain($"src/modules/{LongestNames[0]}/use{Names.ToPascalCase(LongestNames[0])}Samples.ts");

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        var (exitCode, output) = Shell.Run(app, "pnpm", ["exec", "prettier", "--check", .. files]);

        exitCode.ShouldBe(0, output);
    }

    private static IEnumerable<string> ModuleNames() =>
        LongestNames
            .SelectMany(longest => Enumerable.Range(1, longest.Length).Select(length => longest[..length]))
            .Where(name => !name.EndsWith('-'))
            .Distinct();
}

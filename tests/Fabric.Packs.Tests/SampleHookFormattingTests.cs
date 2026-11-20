using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #844: the hook each api-client pack writes for a module's sample, <c>use&lt;Module&gt;Samples.ts</c>, is printed as
/// Prettier prints it at the web app's own <c>printWidth</c> whatever the module is called, so <c>pnpm format:check</c>
/// passes straight after <c>fabric add-module</c>. The profiles' modules have short names, so their own format checks
/// cannot see a line that only breaks, or only joins, for a longer or a shorter one.
/// </summary>
public sealed class SampleHookFormattingTests
{
    /// <summary>
    /// Every prefix of this name that does not end in a hyphen is a module: names from one letter to one whose hook names
    /// no line short enough to fit, with hyphens, so the route (<c>module.name</c>) and the types (<c>module.namespace</c>)
    /// grow at different rates.
    /// </summary>
    private const string LongestName =
        "warehouse-inventory-replenishment-planning-and-order-fulfilment-with-returns-and-customer-notifications";

    /// <summary>Every branch of every api-client pack's hook: the client, and for Vue whether Pinia or TanStack Query holds server data.</summary>
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
    public void The_sample_hook_is_prettier_clean_for_a_module_of_any_name_length(string profile, string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        var record = FabricFile.Read(new StringReader(File.ReadAllText(Path.Combine(project.ProjectDirectory, FabricFile.FileName))), FabricFile.FileName);
        List<string> hooks = [];
        foreach (var name in ModuleNames())
        {
            var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, name, []);
            addition.Errors.ShouldBeEmpty();
            var hook = $"web/app/src/modules/{name}/use{Names.ToPascalCase(name)}Samples.ts";
            project.Write(hook, addition.Created.Single(file => file.Path == hook).Content);
            hooks.Add(hook["web/app/".Length..]);
        }

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), "pnpm", ["exec", "prettier", "--check", .. hooks]);

        exitCode.ShouldBe(0, output);
    }

    private static IEnumerable<string> ModuleNames() =>
        Enumerable.Range(1, LongestName.Length)
            .Select(length => LongestName[..length])
            .Where(name => !name.EndsWith('-'));
}

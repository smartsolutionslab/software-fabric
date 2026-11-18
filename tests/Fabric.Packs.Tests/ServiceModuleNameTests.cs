using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017, #765): a service host knows its module's route name at runtime, the name
/// its module maps its group under and the edge routes by, so a service can map its own endpoints under it
/// (<c>/{module}/mcp</c>, <c>/{module}/agents/{name}</c>) without a line per module.
/// </summary>
public sealed class ServiceModuleNameTests
{
    private const string Profile = "saas-standard-react";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none"];

    /// <summary>Reads, in the generated architecture tests, the name every service host's own ServiceModule.Name gives.</summary>
    private const string ReadNames = """
        namespace Shop.Architecture.Tests;

        public class ServiceModuleNameTests
        {
            [Fact]
            public void Every_service_host_reads_its_modules_route_name()
            {
                var names = ServiceHostEntryPoints.All.ToDictionary(
                    entryPoint => entryPoint.Assembly.GetName().Name!,
                    entryPoint => (string)entryPoint.Assembly.GetType("Microsoft.AspNetCore.Builder.ServiceModule", throwOnError: true)!.GetProperty("Name")!.GetValue(null)!,
                    StringComparer.Ordinal);

                names.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Shop.Services.Billing.Host"] = "billing",
                    ["Shop.Services.OrderItems.Host"] = "order-items",
                });
            }
        }
        """;

    [Fact]
    public void Every_service_host_records_its_modules_route_name_in_its_assembly_and_reads_it_through_one_helper()
    {
        var files = WithModules(Services, "billing", "order-items");

        foreach (var (module, name) in new[] { ("Billing", "billing"), ("OrderItems", "order-items") })
        {
            var project = files[$"src/Services/{module}/Host/Host.csproj"];
            project.ShouldContain($"<ModuleName>{name}</ModuleName>");
            project.ShouldContain("""<AssemblyMetadata Include="ModuleName" Value="$(ModuleName)" />""", customMessage: "the assembly carries the name the AppHost names the service after.");
        }

        var helper = files["src/Host/Wiring/ServiceModule.cs"];
        helper.ShouldContain("namespace Microsoft.AspNetCore.Builder;");
        helper.ShouldContain("public static class ServiceModule");
        helper.ShouldContain("public static string Name");
        helper.ShouldContain("AssemblyMetadataAttribute");
        helper.ShouldNotContain("billing", Case.Insensitive);
        files["tests/Architecture.Tests/Rules/EveryServiceHostKnowsItsModule.cs"].ShouldNotContain("billing", Case.Insensitive);
        files["src/AppHost/AppHost.cs"].ShouldNotContain("billing", Case.Insensitive);
    }

    [Fact]
    public void A_modular_monolith_has_no_service_module_name()
    {
        var files = WithModules(["backend.tenancy=none"], "billing");

        files.Keys.ShouldNotContain("src/Host/Wiring/ServiceModule.cs");
        files.Keys.ShouldNotContain("tests/Architecture.Tests/Rules/EveryServiceHostKnowsItsModule.cs");
        files["src/Host/Host.csproj"].ShouldNotContain("AssemblyMetadata");
    }

    [Fact]
    public void Each_of_two_service_hosts_reads_its_own_modules_route_name_and_one_that_lost_it_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("order-items");
        project.Write("tests/Architecture.Tests/ServiceModuleNameTests.cs", ReadNames);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");

        var path = Path.Combine(project.ProjectDirectory, "src", "Services", "OrderItems", "Host", "Host.csproj");
        var content = File.ReadAllText(path);
        var changed = content.Replace("""<AssemblyMetadata Include="ModuleName" Value="$(ModuleName)" />""", string.Empty, StringComparison.Ordinal);
        changed.ShouldNotBe(content);
        File.WriteAllText(path, changed);
        project.Build();

        (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Services/OrderItems/Host does not know its module's route name: its assembly carries no ModuleName metadata.");
        output.ShouldNotContain("src/Services/Billing/Host does not know");
    }

    private static Dictionary<string, string> WithModules(IReadOnlyList<string> overrides, params string[] modules)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
            var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, module, []);
            addition.Errors.ShouldBeEmpty();
            foreach (var file in addition.Created)
            {
                files.Add(file.Path, file.Content);
            }

            files[FabricFile.FileName] = addition.FabricYaml!;
        }

        return files;
    }
}

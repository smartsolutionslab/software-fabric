using System.Security.Cryptography;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017), part 1: one service host per module, found by the AppHost by convention,
/// so that add-module only creates files; the host slots render into every service host.
/// </summary>
public sealed class ServicesTopologyTests
{
    private const string Profile = "saas-standard-react";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none"];

    [Fact]
    public void A_services_project_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, Services);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Every_module_runs_in_a_service_host_of_its_own_that_references_only_its_Api_project()
    {
        var files = WithModules("billing", "shipping");

        foreach (var module in new[] { "Billing", "Shipping" })
        {
            var project = files[$"src/Services/{module}/Host/Host.csproj"];
            project.ShouldContain($"""<ProjectReference Include="../../../Modules/{module}/{module}.Api/{module}.Api.csproj" />""");
            project.ShouldContain($"<ModuleName>{module.ToLowerInvariant()}</ModuleName>");
            project.ShouldNotContain("Modules/*");
            files.Keys.ShouldContain($"src/Services/{module}/Host/Program.cs");
            files.Keys.ShouldContain($"src/Services/{module}/Host/appsettings.json");
            files.Keys.ShouldContain($"src/Services/{module}/Host/Properties/launchSettings.json");
        }

        files.Keys.ShouldNotContain("src/Host/Host.csproj");
        files.Keys.ShouldNotContain("src/Host/Program.cs");
    }

    [Fact]
    public void The_host_slots_render_into_every_service_host()
    {
        var files = WithModules("billing", "shipping");

        foreach (var module in new[] { "Billing", "Shipping" })
        {
            var program = files[$"src/Services/{module}/Host/Program.cs"];
            program.ShouldContain("ModuleDiscovery.Discover(AppContext.BaseDirectory)");
            program.ShouldContain("AddKeycloakAuthentication", customMessage: "keycloak's host.services contribution");
            program.ShouldContain("UseAuthorization", customMessage: "a host.middleware contribution");
            files[$"src/Services/{module}/Host/Host.csproj"].ShouldContain("""<PackageReference Include="WolverineFx""", customMessage: "a host.packages contribution");
            files[$"src/Services/{module}/Host/appsettings.json"].ShouldContain("Microsoft.EntityFrameworkCore.Database.Command", customMessage: "ef-core's host.log-levels contribution");
        }
    }

    [Fact]
    public void The_AppHost_adds_every_service_host_by_convention_with_the_host_wiring_and_has_no_line_per_module()
    {
        var files = WithModules("billing", "shipping");
        var appHost = files["src/AppHost/AppHost.cs"];

        appHost.ShouldContain("foreach (var (module, project) in builder.ServiceHosts())");
        appHost.ShouldContain("builder.AddProject(module, project)");
        appHost.ShouldContain(".WithReference(messaging)");
        appHost.ShouldNotContain("billing", Case.Insensitive);
        appHost.ShouldNotContain("shipping", Case.Insensitive);
        files["src/AppHost/AppHost.csproj"].ShouldContain("""<ProjectReference Include="../Services/*/Host/Host.csproj" """);
        files["src/AppHost/AppHost.csproj"].ShouldNotContain("Billing");
    }

    [Fact]
    public void A_services_project_with_two_modules_builds_and_add_module_adds_a_third_service_without_changing_an_existing_file()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("shipping");
        var before = Snapshot(project.ProjectDirectory);

        project.AddModule("returns");

        var after = Snapshot(project.ProjectDirectory);
        // add-module records the module in fabric.yaml and inserts its sample's endpoints into the frontend's copy of the
        // OpenAPI document (#734); every other existing file stays as it was.
        var changed = before.Where(file => file.Key != FabricFile.FileName && file.Key != "web/app/openapi.json" && after[file.Key] != file.Value).Select(file => file.Key).ToList();
        changed.ShouldBeEmpty();
        after.Keys.ShouldContain("src/Services/Returns/Host/Host.csproj");

        project.Build();
        foreach (var tests in new[] { "tests/Architecture.Tests/Architecture.Tests.csproj", "tests/Modules/Billing.Tests/Billing.Tests.csproj", "tests/Modules/Shipping.Tests/Shipping.Tests.csproj", "tests/Modules/Returns.Tests/Returns.Tests.csproj" })
        {
            var (exitCode, output) = project.Test(tests);
            exitCode.ShouldBe(0, output);
            output.ShouldContain("failed: 0");
        }

        File.Exists(Path.Combine(project.ProjectDirectory, "src", "Services", "Returns", "Host", "bin", "Release", GeneratedProject.TargetFramework, "Shop.Modules.Returns.Api.dll"))
            .ShouldBeTrue("building the AppHost's references builds the third service host with its module.");
    }

    [Fact]
    public void A_service_host_that_references_another_module_or_none_fails_the_architecture_tests_naming_it()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("shipping");
        Rewrite(project, "src/Services/Billing/Host/Host.csproj", content =>
            content.Replace(
                """<ProjectReference Include="../../../Modules/Billing/Billing.Api/Billing.Api.csproj" />""",
                """<ProjectReference Include="../../../Modules/*/*.Api/*.Api.csproj" />""",
                StringComparison.Ordinal));
        Rewrite(project, "src/Services/Shipping/Host/Host.csproj", content =>
            content.Replace(
                """<ProjectReference Include="../../../Modules/Shipping/Shipping.Api/Shipping.Api.csproj" />""",
                string.Empty,
                StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Services/Billing/Host/Host.csproj references src/Modules/Shipping/Shipping.Api/Shipping.Api.csproj, a project of the module Shipping.");
        output.ShouldContain("src/Services/Billing/Host finds 2 IModule implementations, in Shop.Modules.Billing.Api, Shop.Modules.Shipping.Api; a service host runs exactly one module.");
        output.ShouldContain("src/Services/Shipping/Host finds 0 IModule implementations, in no module Api project; a service host runs exactly one module.");
        output.ShouldNotContain("src/Services/Shipping/Host/Host.csproj references");
    }

    private static Dictionary<string, string> WithModules(params string[] modules)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, Services);
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

    private static void Rewrite(GeneratedProject project, string relativePath, Func<string, string> change)
    {
        var path = Path.Combine(project.ProjectDirectory, relativePath);
        var content = File.ReadAllText(path);
        var changed = change(content);
        changed.ShouldNotBe(content, $"{relativePath} did not change.");
        File.WriteAllText(path, changed);
    }

    private static Dictionary<string, string> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(
                path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);
}

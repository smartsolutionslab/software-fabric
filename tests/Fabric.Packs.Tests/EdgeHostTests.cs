using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017), part 2: the edge, a YARP host of its own in front of the services (D1).
/// It routes <c>/{module}/**</c> to the module's service by the convention that names the services, carries what exists
/// once per application, and forwards the bearer token unchanged to services that validate it themselves (D4).
/// </summary>
public sealed class EdgeHostTests
{
    private const string Profile = "saas-standard-react";

    private const string Gate = "tests/Architecture.Tests/Rules/SingletonsLiveOnTheEdge.cs";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none"];

    [Fact]
    public void The_edge_is_a_YARP_host_of_its_own_whose_route_table_names_no_module()
    {
        var files = WithModules("billing", "shipping");

        var project = files["src/Edge/Edge.csproj"];
        project.ShouldContain("""<PackageReference Include="Yarp.ReverseProxy" />""");
        project.ShouldContain("""<PackageReference Include="Microsoft.Extensions.ServiceDiscovery.Yarp" />""");
        project.ShouldNotContain("Modules/", customMessage: "the edge runs no module code.");
        project.ShouldContain("<AssemblyName>Host</AssemblyName>", customMessage: "the OpenAPI document is named, and tags its operations, after the host, as the monolith's copy does.");
        files["Directory.Packages.props"].ShouldContain("""<PackageVersion Include="Yarp.ReverseProxy" """);
        files["Directory.Packages.props"].ShouldContain("""<PackageVersion Include="Microsoft.Extensions.ServiceDiscovery.Yarp" """);
        files["Solution.slnx"].ShouldContain("""<Project Path="src/Edge/Edge.csproj" />""");

        var program = files["src/Edge/Program.cs"];
        program.ShouldContain("builder.Services.AddModuleRoutes(builder.Configuration);");
        program.ShouldContain("app.MapReverseProxy();");
        files.Keys.ShouldContain("src/Edge/appsettings.json");
        files.Keys.ShouldContain("src/Edge/Properties/launchSettings.json");
        foreach (var file in files.Where(file => file.Key.StartsWith("src/Edge/", StringComparison.Ordinal)))
        {
            file.Value.ShouldNotContain("billing", Case.Insensitive, $"{file.Key} names a module; the route table comes from the convention.");
            file.Value.ShouldNotContain("shipping", Case.Insensitive, $"{file.Key} names a module; the route table comes from the convention.");
        }
    }

    [Fact]
    public void The_AppHost_adds_the_edge_with_its_wiring_and_hands_it_every_service_inside_the_loop()
    {
        var files = WithModules("billing", "shipping");
        var appHost = files["src/AppHost/AppHost.cs"];

        var edge = appHost.IndexOf("""var host = builder.AddBuiltProject("host", "../Edge/Edge.csproj")""", StringComparison.Ordinal);
        var loop = appHost.IndexOf("foreach (var (module, project) in builder.ServiceHosts())", StringComparison.Ordinal);
        edge.ShouldBeGreaterThanOrEqualTo(0, appHost);
        loop.ShouldBeGreaterThan(edge, "the loop hands each service to the edge, so the edge comes first.");
        appHost[edge..loop].ShouldContain("Authentication__Authority", customMessage: "keycloak's apphost.edge-wiring: the edge validates the token for its own endpoints.");
        appHost[edge..loop].ShouldNotContain("WithReference(messaging)", customMessage: "the services' wiring is not the edge's.");
        appHost[loop..].ShouldContain("host.WithReference(service)");
        appHost[loop..].ShouldContain("""WithEnvironment($"Edge__Modules__{module}", module)""");
        appHost.ShouldNotContain("billing", Case.Insensitive);
        files["src/AppHost/AppHost.csproj"].ShouldContain("""<ProjectReference Include="../Edge/Edge.csproj" """);
    }

    [Fact]
    public void What_exists_once_maps_on_the_edge_which_validates_the_token_as_every_service_does()
    {
        var files = WithModules("billing", "shipping");

        var edge = files["src/Edge/Program.cs"];
        edge.ShouldContain("""app.MapGet("/me", """, customMessage: "the base's edge.endpoints contribution");
        edge.ShouldContain("AddKeycloakAuthentication", customMessage: "keycloak's edge.services contribution");
        edge.ShouldContain("app.UseAuthentication();", customMessage: "keycloak's edge.middleware contribution");
        edge.ShouldContain("AddOpenApi", customMessage: "rest's edge.services contribution");
        edge.ShouldContain("app.UseServiceDocuments();", customMessage: "the edge serves one OpenAPI document with every service's paths");
        files["src/Edge/Edge.csproj"].ShouldContain("""<Compile Include="../Host/**/*.cs" """, customMessage: "the code packs write once for every host");
        foreach (var module in new[] { "Billing", "Shipping" })
        {
            var service = files[$"src/Services/{module}/Host/Program.cs"];
            service.ShouldNotContain("\"/me\"");
            service.ShouldContain("AddKeycloakAuthentication", customMessage: "every service still validates the forwarded token.");
        }

        files.Keys.ShouldContain(Gate);
    }

    [Fact]
    public void A_modular_monolith_has_no_edge_and_keeps_me_on_its_host()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, []);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);

        files.Keys.ShouldNotContain(path => path.StartsWith("src/Edge/", StringComparison.Ordinal));
        files.Keys.ShouldNotContain(Gate);
        files["src/Host/Program.cs"].ShouldContain("""app.MapGet("/me", """);
        files["src/AppHost/AppHost.cs"].ShouldNotContain("Edge");
    }

    [Fact]
    public void A_service_host_that_maps_me_fails_SingletonsLiveOnTheEdge_naming_it()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("shipping");
        var program = Path.Combine(project.ProjectDirectory, "src", "Services", "Billing", "Host", "Program.cs");
        var content = File.ReadAllText(program);
        content.ShouldContain("app.MapDefaultEndpoints();\n");
        File.WriteAllText(program, content.Replace(
            "app.MapDefaultEndpoints();\n",
            "app.MapDefaultEndpoints();\napp.MapGet(\"/me\", () => TypedResults.Ok()).RequireAuthorization();\n",
            StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Services/Billing/Host maps GET /me, which the application has once: map it on the edge (src/Edge) through edge.endpoints.");
        output.ShouldNotContain("src/Services/Shipping/Host maps");
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
}

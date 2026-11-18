using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017), D2: a database per service on one server. The database packs declare
/// the server alone; the AppHost's loop over the service hosts gives every service a database of its own, named after its
/// module, under the connection name it already reads, and a MigrationRunner run of its own that migrates the module's
/// schema, its event store included, into that database alone.
/// </summary>
public sealed class ServiceDatabasesTests
{
    private const string Profile = "saas-standard-react";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none", "backend.authorization=roles"];

    [Theory]
    [InlineData("postgres", "var postgres = builder.AddPostgres(\"postgres\")", "var database = postgres.AddDatabase($\"{module}-database\", module);")]
    [InlineData("sqlserver", "var sqlServer = builder.AddSqlServer(\"sqlserver\")", "var database = sqlServer.AddDatabase($\"{module}-database\", module);")]
    public void The_database_pack_declares_the_server_alone_and_the_loop_gives_every_service_a_database_of_its_own(string database, string server, string perService)
    {
        var appHost = WithModules([.. Services, $"backend.database={database}"], "billing", "shipping")["src/AppHost/AppHost.cs"];

        appHost.ShouldContain(server);
        appHost.ShouldNotContain(".AddDatabase(\"database\")", customMessage: "with services there is no single database.");
        var loop = appHost.IndexOf("foreach (var (module, project) in builder.ServiceHosts())", StringComparison.Ordinal);
        var service = appHost.IndexOf("var service = builder.AddProject(module, project)", StringComparison.Ordinal);
        appHost.IndexOf(perService, StringComparison.Ordinal).ShouldBeInRange(loop, service, "the loop adds the service's database before the service.");
        var serviceWiring = appHost[service..appHost.IndexOf("host.WithReference(service)", StringComparison.Ordinal)];
        serviceWiring.ShouldContain(".WithReference(database, \"database\")", customMessage: "the service reads its own database under the connection name it already reads.");
        serviceWiring.ShouldContain(".WaitFor(database)");
        appHost.ShouldNotContain("billing", Case.Insensitive);
        appHost.ShouldNotContain("shipping", Case.Insensitive);
    }

    [Fact]
    public void With_rag_the_server_keeps_the_pgvector_image_and_its_volume()
    {
        var appHost = WithModules([.. Services, "backend.llm=openai", "backend.llm.extras=rag"], "billing")["src/AppHost/AppHost.cs"];

        appHost.ShouldContain("var postgres = builder.AddPostgres(\"postgres\")\n    .WithImage(\"pgvector/pgvector\", \"0.8.7-pg18\");");
        appHost.ShouldContain("postgres\n    .WithVolume(VolumeNameGenerator.Generate(postgres, \"data\"), \"/var/lib/postgresql\");");
        appHost.ShouldNotContain(".AddDatabase(\"database\")");
        appHost.ShouldContain("var database = postgres.AddDatabase($\"{module}-database\", module);");
    }

    [Fact]
    public void Every_service_waits_for_a_migration_run_of_its_own_against_its_own_database()
    {
        var appHost = WithModules(Services, "billing", "shipping")["src/AppHost/AppHost.cs"];

        appHost.ShouldNotContain("AddProject<Projects.MigrationRunner>(\"migrations\")", customMessage: "with services no run migrates every module.");
        var loop = appHost.IndexOf("foreach (var (module, project) in builder.ServiceHosts())", StringComparison.Ordinal);
        var service = appHost.IndexOf("var service = builder.AddProject(module, project)", StringComparison.Ordinal);
        var migrations = appHost.IndexOf("var migrations = builder.AddProject<Projects.MigrationRunner>($\"{module}-migrations\")", StringComparison.Ordinal);
        migrations.ShouldBeInRange(loop, service, "the loop adds the service's migration run before the service.");
        var run = appHost[migrations..service];
        run.ShouldContain(".WithReference(database, \"database\")");
        run.ShouldContain(".WithEnvironment(\"Migrations__Module\", module)");
        run.ShouldContain(".WaitFor(database)");
        appHost[service..].ShouldContain(".WaitForCompletion(migrations)");
    }

    [Fact]
    public void With_services_the_MigrationRunner_migrates_the_one_module_the_AppHost_names_and_fails_without_it()
    {
        var program = WithModules(Services, "billing")["src/MigrationRunner/Program.cs"];

        program.ShouldContain("builder.Configuration[\"Migrations:Module\"]");
        program.ShouldContain("throw new InvalidOperationException(");
        program.ShouldNotContain("\"Shop.Modules.*.Infrastructure.dll\"", customMessage: "a run migrates its module's database alone, never every module's schema.");
        program.ShouldContain("Shop.Modules.{");
    }

    [Fact]
    public void With_services_Martens_step_creates_the_event_store_of_the_runs_module_alone()
    {
        var files = Mixed([.. Services]);

        files["src/MigrationRunner/Program.cs"].ShouldContain("await host.Services.ApplyEventStoreSchemasAsync(infrastructure);");
        var registration = files["src/BuildingBlocks/EventStore/Wiring/EventStoreRegistration.cs"];
        registration.ShouldContain("ApplyEventStoreSchemasAsync(this IServiceProvider services, Assembly infrastructure)");
        registration.ShouldNotContain("Modules.*.Infrastructure.dll");
    }

    [Fact]
    public void With_services_the_aspire_fixtures_database_is_one_of_the_tests_own_on_the_AppHosts_server()
    {
        var fixture = WithModules([.. Services, "backend.data-access.reads=dapper"], "billing")["tests/Reads.Tests/ApplicationDatabase.cs"];

        fixture.ShouldContain("builder.CreateResourceBuilder(postgres).AddDatabase(\"database\")");
        fixture.ShouldNotContain("MigrationRunner", customMessage: "the tests' database is no service's; no module's run migrates it.");
    }

    [Fact]
    public void A_modular_monolith_keeps_its_one_database_and_one_migration_run()
    {
        var files = WithModules(["backend.tenancy=none", "backend.authorization=roles"], "billing");

        files["src/AppHost/AppHost.cs"].ShouldContain(".AddDatabase(\"database\");");
        files["src/AppHost/AppHost.cs"].ShouldContain("builder.AddProject<Projects.MigrationRunner>(\"migrations\")");
        files["src/AppHost/AppHost.cs"].ShouldNotContain("{module}-database");
        files["src/MigrationRunner/Program.cs"].ShouldNotContain("Migrations:Module");
    }

    [Fact]
    public void A_services_project_with_a_ddd_and_an_event_sourced_module_builds_and_passes_its_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, [.. Services, "backend.data-access.reads=dapper", "module.style=event-sourced", "module.event-store=marten"]);
        foreach (var (path, content) in Mixed([.. Services, "backend.data-access.reads=dapper"]))
        {
            project.Write(path, content);
        }

        project.Build();
        foreach (var tests in new[] { "tests/Architecture.Tests/Architecture.Tests.csproj", "tests/Modules/Billing.Tests/Billing.Tests.csproj", "tests/Modules/Ledger.Tests/Ledger.Tests.csproj" })
        {
            var (exitCode, output) = project.Test(tests);
            exitCode.ShouldBe(0, output);
            output.ShouldContain("failed: 0");
        }
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

    /// <summary>
    /// A project with the event-sourced module ledger on Marten and the ddd module billing. add-module refuses a module
    /// whose style is new to the project (its pack writes project-wide files), so the whole project is composed with both.
    /// </summary>
    private static Dictionary<string, string> Mixed(IReadOnlyList<string> overrides)
    {
        var files = WithModules([.. overrides, "module.style=event-sourced", "module.event-store=marten"], "ledger");
        var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
        var billing = ChoiceResolver.ResolveModule(Repository.Catalog, record.Choices, ["module.style=ddd"]);
        billing.Errors.ShouldBeEmpty();
        OrderedDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules = [];
        foreach (var (name, values) in record.Choices.Modules)
        {
            modules.Add(name, values);
        }

        modules.Add("billing", billing.Values!);
        var choices = record.Choices with { Modules = modules };
        var composition = Composer.Compose(Repository.Catalog, Repository.Packs.Packs, choices);
        composition.Errors.ShouldBeEmpty();
        var rendered = Renderer.Render(Repository.Catalog, composition, choices, "shop");
        rendered.Errors.ShouldBeEmpty();
        foreach (var file in rendered.Files)
        {
            files[file.Path] = file.Content;
        }

        files[FabricFile.FileName] = FabricFile.Write(Repository.Catalog, "shop", Profile, choices);
        return files;
    }
}

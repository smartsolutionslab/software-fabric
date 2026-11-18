using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> (ADR-0017) with <c>backend.llm.extras: rag</c> (ADR-0012): every service's
/// MigrationRunner run makes its module's vector table in the service's own database (D2), so the generated
/// <c>VectorTablesTests</c> look for each module's table there, after the fixture has let every service's migration
/// run finish, not in the tests' own database, which no run migrates. No test here calls a model.
/// </summary>
public sealed class ServicesRagTests
{
    private const string Profile = "saas-standard-react";
    private const string Fixture = "tests/Rag.Tests/ApplicationDatabase.cs";
    private const string VectorTables = "tests/Rag.Tests/VectorTablesTests.cs";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none", "backend.llm=openai", "backend.llm.extras=rag"];

    [Fact]
    public void With_services_the_rag_tests_fixture_waits_for_every_services_migration_run_and_names_each_services_database()
    {
        var files = Files(Services);
        var fixture = files[Fixture];

        fixture.ShouldContain("builder.CreateResourceBuilder(postgres).AddDatabase(\"database\")", customMessage: "the vector index tests keep a database of their own.");
        fixture.ShouldContain("-migrations", customMessage: "every service's migration run makes its module's vector table.");
        fixture.ShouldContain("public IReadOnlyDictionary<string, string> ServiceDatabases");
        files[VectorTables].ShouldContain("database.ServiceDatabases");
    }

    [Fact]
    public void With_services_and_no_rag_the_fixture_still_runs_no_migrations()
    {
        var fixture = Files(["backend.topology=services", "backend.tenancy=none", "backend.data-access.reads=dapper"])["tests/Reads.Tests/ApplicationDatabase.cs"];

        fixture.ShouldNotContain("MigrationRunner");
        fixture.ShouldNotContain("-migrations");
        fixture.ShouldNotContain("ServiceDatabases");
    }

    [Fact]
    public void A_modular_monolith_rag_tests_read_the_one_migrated_database()
    {
        var files = Files(["backend.tenancy=none", "backend.llm=openai", "backend.llm.extras=rag"]);

        files[Fixture].ShouldContain("private const string MigrationRunner = \"migrations\";");
        files[Fixture].ShouldNotContain("ServiceDatabases");
        files[VectorTables].ShouldNotContain("ServiceDatabases");
    }

    [Fact]
    public void The_generated_rag_tests_of_a_services_project_find_every_modules_vector_table_in_its_services_own_database()
    {
        using var project = new GeneratedProject(Profile, Services);
        project.AddModule("billing");
        project.AddModule("order-handling");
        project.Build();

        var (ragExitCode, ragOutput) = project.Test("tests/Rag.Tests/Rag.Tests.csproj");
        ragExitCode.ShouldBe(0, ragOutput);
        ragOutput.ShouldContain("failed: 0");
        ragOutput.ShouldContain("succeeded: 6");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    private static Dictionary<string, string> Files(string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
    }
}

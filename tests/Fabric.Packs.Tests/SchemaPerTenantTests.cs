using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.tenancy: schema-per-tenant</c> (#253): every module keeps each tenant's rows in a schema of its own,
/// <c>&lt;module&gt;_&lt;tenant&gt;</c>, for the tenants in <c>Tenancy:Tenants</c>, which the AppHost sets. The
/// MigrationRunner creates and migrates those schemas at startup, each with its own migrations history, after checking
/// each module's model against its snapshot in the module's own schema; the host never creates one. A tenant not in the
/// list, like no tenant, works in the module's own schema, where it reads nothing and cannot save.
/// </summary>
public sealed class SchemaPerTenantTests
{
    private const string Profile = "saas-standard-react";
    private const string SchemaPerTenant = "backend.tenancy=schema-per-tenant";
    private const string DatabasePassword = "fabric-tests";

    [Fact]
    public void Every_module_context_works_in_its_tenants_schema_and_the_apphost_lists_the_tenants_for_the_runner_and_the_host()
    {
        var files = Files(Profile, SchemaPerTenant);

        files["src/BuildingBlocks/Persistence/ModuleDbContext.cs"].ShouldContain("public string CurrentSchema { get; } = Shop.BuildingBlocks.Tenancy.TenantSchemas.Of(schema, services);");
        files["src/BuildingBlocks/Persistence/ModuleDbContext.cs"].ShouldNotContain("TenantColumn");
        files["src/MigrationRunner/Program.cs"].ShouldContain("await host.Services.MigrateTenantSchemasAsync(contexts);");
        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("string[] tenants = [\"tenant-a\", \"tenant-b\"];");
        appHost.ShouldContain("migrations.WithTenants(tenants);");
        appHost.ShouldContain("    .WithTenants(tenants)");
        files.Keys.ShouldNotContain("src/BuildingBlocks/Tenancy/Wiring/TenantColumn.cs");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/NoTenantIds.cs");
        files["Solution.slnx"].ShouldContain("tests/Tenancy.Tests/Tenancy.Tests.csproj");
    }

    [Fact]
    public void The_shared_tenancy_files_are_the_column_packs_own()
    {
        var column = Files(Profile);
        var schema = Files(Profile, SchemaPerTenant);

        foreach (var path in (string[])["src/BuildingBlocks/Tenancy/ITenantContext.cs", "src/BuildingBlocks/Tenancy/TenantScope.cs", "src/BuildingBlocks/Tenancy/HttpTenantContext.cs", "src/BuildingBlocks/Tenancy/Wiring/TenancyRegistration.cs", "src/Host/CurrentTenant.cs"])
        {
            schema[path].ShouldBe(column[path], path);
        }

        schema["src/Host/Program.cs"].ShouldContain("app.MapGet(\"/me/tenant\"");
    }

    [Fact]
    public void Under_testcontainers_the_database_and_the_host_get_the_tenants_before_the_runner_runs()
    {
        var files = Files(Profile, SchemaPerTenant, "backend.test.integration=testcontainers");

        foreach (var fixture in (string[])["tests/Integration.Tests/ContainerFixture.cs", "tests/Tenancy.Tests/ApplicationDatabase.cs"])
        {
            var content = files[fixture];
            var tenants = content.IndexOf("settings[\"Tenancy:Tenants:0\"] = \"tenant-a\";", StringComparison.Ordinal);
            tenants.ShouldBeGreaterThan(0, fixture);
            content.ShouldContain("settings[\"Tenancy:Tenants:1\"] = \"tenant-b\";", customMessage: fixture);
            content.IndexOf("RunProjectAsync(\"src/MigrationRunner\"", StringComparison.Ordinal).ShouldBeGreaterThan(tenants, fixture);
        }
    }

    [Fact]
    public void Dashboards_have_no_TenantId_column_under_schema_per_tenant_and_keep_it_under_a_tenant_column()
    {
        const string Dashboards = "frontend.dashboards=user-editable";
        const string Migrations = "src/Modules/Dashboards/Dashboards.Infrastructure/Migrations/";
        var schema = Files(Profile, SchemaPerTenant, Dashboards);
        var column = Files(Profile, Dashboards);

        var migrations = schema.Where(file => file.Key.StartsWith(Migrations, StringComparison.Ordinal)).ToList();
        migrations.Count.ShouldBe(3);
        migrations.ShouldAllBe(file => !file.Value.Contains("TenantId", StringComparison.Ordinal));
        column.Where(file => file.Key.StartsWith(Migrations, StringComparison.Ordinal)).ShouldAllBe(file => file.Value.Contains("TenantId", StringComparison.Ordinal));
        schema["src/Modules/Dashboards/Dashboards.Infrastructure/DashboardStore.cs"].ShouldContain("and by tenant in the tenant's own schema");
    }

    [Theory]
    [InlineData("frontend.realtime=signalr", "src/Host/Realtime/EventsHub.cs", "#error The signalr pack groups browsers by tenant for backend.tenancy column only so far")]
    [InlineData("frontend.realtime=sse", "src/Host/Realtime/BrowserEventStreams.cs", "#error The sse pack sends to a tenant's streams for backend.tenancy column only so far")]
    public void Signalr_and_sse_still_refuse_schema_per_tenant_until_their_own_branch(string realtime, string path, string refusal)
    {
        Files(Profile, SchemaPerTenant, realtime)[path].ShouldStartWith(refusal);
    }

    [Fact]
    public void The_in_process_outbox_refuses_schema_per_tenant_instead_of_writing_where_no_table_is()
    {
        var files = Files("lean-crud", SchemaPerTenant);

        files["src/BuildingBlocks/Messaging/Outbox.cs"].ShouldStartWith("#error The in-process outbox of backend.bus.transport.none keeps one table per module schema, so it supports backend.tenancy none and column only so far");
    }

    [Fact]
    public void The_migration_runner_creates_and_migrates_every_modules_schema_for_each_listed_tenant_with_its_own_history()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldBe(0, output);
        foreach (var schema in (string[])["billing_tenant_a", "billing_tenant_b"])
        {
            postgres.Psql(DatabasePassword, $"select count(*) from information_schema.tables where table_schema = '{schema}' and table_name = 'notes'").ShouldBe("1", schema);
            postgres.Psql(DatabasePassword, $"select \"MigrationId\" from {schema}.\"__EFMigrationsHistory\"").ShouldEndWith("_Initial", customMessage: schema);
        }

        // The module's own schema, checked against the snapshot first, holds the rows of no tenant.
        postgres.Psql(DatabasePassword, "select count(*) from billing.notes").ShouldBe("0");
        postgres.Psql(DatabasePassword, "select count(*) from information_schema.schemata where schema_name like 'billing\\_%'").ShouldBe("2");
    }

    [Fact]
    public void A_model_without_its_migration_fails_the_runner_before_any_tenant_schema_exists()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/NoteConfiguration.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
            {
                public void Configure(EntityTypeBuilder<Note> builder)
                {
                    builder.ToTable("notes");
                    builder.HasKey(note => note.Id);
                    builder.Property(note => note.Text).HasMaxLength(200);
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("PendingModelChangesWarning");
        postgres.Psql(DatabasePassword, "select count(*) from information_schema.schemata where schema_name like 'billing\\_%'").ShouldBe("0");
    }

    [Fact]
    public void The_generated_tenancy_tests_show_no_tenant_reads_another_tenants_rows_and_the_architecture_rules_pass()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        project.Build();

        var (tenancyExitCode, tenancyOutput) = project.Test("tests/Tenancy.Tests/Tenancy.Tests.csproj");
        tenancyExitCode.ShouldBe(0, tenancyOutput);
        tenancyOutput.ShouldContain("succeeded: 8");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
    }

    private static (int ExitCode, string Output) RunMigrationRunner(GeneratedProject project, Container postgres) =>
        project.DotNetCommand(
            new Dictionary<string, string>
            {
                ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
                ["Tenancy__Tenants__0"] = "tenant-a",
                ["Tenancy__Tenants__1"] = "tenant-b",
            },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

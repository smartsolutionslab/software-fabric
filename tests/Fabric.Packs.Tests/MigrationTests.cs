namespace Fabric.Packs.Tests;

public sealed class MigrationTests
{
    private const string Profile = "saas-standard-react";
    private const string DatabasePassword = "fabric-tests";

    [Fact]
    public void A_migration_made_with_the_ef_tool_is_applied_by_the_migration_runner_in_the_module_schema()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        AddNotesWithAMigration(project);

        using var postgres = Container.Postgres(DatabasePassword);
        var (runExitCode, runOutput) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        runExitCode.ShouldBe(0, runOutput);

        postgres.Psql(DatabasePassword, "select count(*) from information_schema.tables where table_schema = 'billing' and table_name = 'notes'").ShouldBe("1");
        postgres.Psql(DatabasePassword, "select \"MigrationId\" from billing.\"__EFMigrationsHistory\"").ShouldEndWith("_Initial");
    }

    [Fact]
    public void A_step_another_pack_puts_in_the_migration_runner_runs_after_the_migrations_with_the_runners_services()
    {
        // mantine is deferred and has no pack, so the probe stands in for it and fills the slot; shadcn, which it displaces, writes only under web/. It reads a table only a migration creates.
        using var probe = new ProbePack("frontend.ui-library.mantine", new Dictionary<string, string>
        {
            ["migration-runner.steps"] = """
                // Fails with 42P01 unless the migrations have created billing.notes.
                await using (var probeScope = host.Services.CreateAsyncScope())
                {
                    await probeScope.ServiceProvider.GetRequiredService<Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext>()
                        .Database.ExecuteSqlRawAsync("select count(*) from billing.notes");
                }

                var configured = host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>().GetConnectionString("database") is not null;
                Console.WriteLine($"probe step ran after the migrations; database configured: {configured}");
                """,
        });
        using var project = new GeneratedProject(Profile, [probe.Manifest], "frontend.ui-library=mantine");
        project.AddModule("billing");
        AddNotesWithAMigration(project);

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("probe step ran after the migrations; database configured: True");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_schema_another_pack_gives_a_module_context_holds_its_model_its_migrations_and_their_history(bool tableNamesTheModulesSchema)
    {
        // As above, the probe stands in for mantine. Its schema comes from AppContext, which its step sets to two schemas in
        // turn: a context's tables, its migrations and their history all follow, and each schema gets a model of its own.
        using var probe = new ProbePack("frontend.ui-library.mantine", new Dictionary<string, string>
        {
            ["persistence.schema"] = """AppContext.GetData("probe-schema") as string ?? schema""",
            ["migration-runner.steps"] = """
                foreach (var probeSchema in (string[])["billing_one", "billing_two"])
                {
                    AppContext.SetData("probe-schema", probeSchema);
                    await using var probeScope = host.Services.CreateAsyncScope();
                    var billing = probeScope.ServiceProvider.GetRequiredService<Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext>();
                    await billing.Database.MigrateAsync();
                    billing.Add(new Shop.Modules.Billing.Domain.Note(Guid.CreateVersion7(), probeSchema));
                    await billing.SaveChangesAsync();
                }
                """,
        });
        using var project = new GeneratedProject(Profile, [probe.Manifest], "frontend.ui-library=mantine", "backend.tenancy=none");
        project.AddModule("billing");
        AddNotesWithAMigration(project, tableNamesTheModulesSchema);

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");

        exitCode.ShouldBe(0, output);
        postgres.Psql(DatabasePassword, "select \"Text\" from billing_one.notes").ShouldBe("billing_one");
        postgres.Psql(DatabasePassword, "select \"Text\" from billing_two.notes").ShouldBe("billing_two");
        postgres.Psql(DatabasePassword, "select \"MigrationId\" from billing_one.\"__EFMigrationsHistory\"").ShouldEndWith("_Initial");
        postgres.Psql(DatabasePassword, "select \"MigrationId\" from billing_two.\"__EFMigrationsHistory\"").ShouldEndWith("_Initial");
        postgres.Psql(DatabasePassword, "select count(*) from billing.notes").ShouldBe("0");
    }

    [Fact]
    public void A_migration_naming_the_modules_schema_in_a_provider_annotation_fails_the_move_instead_of_running_unmoved()
    {
        // A PostgreSQL enum lives in the module's schema; Npgsql names it in an annotation, which no schema property carries.
        using var probe = new ProbePack("frontend.ui-library.mantine", new Dictionary<string, string>
        {
            ["persistence.schema"] = """AppContext.GetData("probe-schema") as string ?? schema""",
            ["migration-runner.steps"] = """
                AppContext.SetData("probe-schema", "billing_one");
                await using var probeScope = host.Services.CreateAsyncScope();
                await probeScope.ServiceProvider.GetRequiredService<Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext>().Database.MigrateAsync();
                """,
        });
        using var project = new GeneratedProject(Profile, [probe.Manifest], "frontend.ui-library=mantine", "backend.tenancy=none");
        project.AddModule("billing");
        AddNotesWithAMigration(project);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Migrations/20990101000000_Mood.cs", """
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Microsoft.EntityFrameworkCore.Migrations;

            namespace Shop.Modules.Billing.Infrastructure.Migrations;

            [DbContext(typeof(Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext))]
            [Migration("20990101000000_Mood")]
            public sealed class Mood : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                    Ensure.That(migrationBuilder).IsNotNull();

                    migrationBuilder.AlterDatabase().Annotation("Npgsql:Enum:billing.mood", "happy,sad");
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("20990101000000_Mood");
        output.ShouldContain("Npgsql:Enum:billing.mood");
        // Refused in the module's own schema already, before any migration ran anywhere.
        postgres.Psql(DatabasePassword, "select count(*) from information_schema.tables where table_name = 'notes'").ShouldBe("0");
    }

    [Fact]
    public void Without_a_schema_from_another_pack_the_module_context_works_in_the_modules_schema_alone()
    {
        using var project = new GeneratedProject(Profile);

        var context = File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "BuildingBlocks", "Persistence", "ModuleDbContext.cs"));
        context.ShouldContain("modelBuilder.HasDefaultSchema(schema);");
        context.ShouldNotContain("CurrentSchema");
        context.ShouldNotContain("ReplaceService");
        File.Exists(Path.Combine(project.ProjectDirectory, "src", "BuildingBlocks", "Persistence", "SchemaMigrationsAssembly.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "src", "BuildingBlocks", "Persistence", "SchemaModelCacheKeyFactory.cs")).ShouldBeFalse();
    }

    [Fact]
    public void Without_a_step_the_migration_runner_ends_with_the_migrations()
    {
        using var project = new GeneratedProject(Profile);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "MigrationRunner", "Program.cs")).ShouldEndWith("""
                await ((DbContext)scope.ServiceProvider.GetRequiredService(context)).Database.MigrateAsync();
            }

            """);
    }

    /// <summary>A Note entity in billing, a migration creating its table made with the EF tool, and a build.</summary>
    /// <param name="tableNamesTheModulesSchema">Maps the table with ToTable("notes", BillingDbContext.Schema), not by the default schema.</param>
    internal static void AddNotesWithAMigration(GeneratedProject project, bool tableNamesTheModulesSchema = false)
    {
        project.Write("src/Modules/Billing/Billing.Domain/Note.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain;

            public sealed class Note(Guid id, string text) : Entity<Guid>(id)
            {
                public string Text { get; private set; } = text;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/NoteConfiguration.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
            {
                public void Configure(EntityTypeBuilder<Note> builder)
                {
                    builder.ToTable({{TABLE}});
                    builder.HasKey(note => note.Id);
                    builder.Property(note => note.Text);
                }
            }
            """.Replace("{{TABLE}}", tableNamesTheModulesSchema ? "\"notes\", BillingDbContext.Schema" : "\"notes\"", StringComparison.Ordinal));
        project.Build();

        var none = new Dictionary<string, string>();
        var (restoreExitCode, restoreOutput) = project.DotNetCommand(none, "tool", "restore");
        restoreExitCode.ShouldBe(0, restoreOutput);
        var (addExitCode, addOutput) = project.DotNetCommand(
            none,
            "ef", "migrations", "add", "Initial",
            "--project", "src/Modules/Billing/Billing.Infrastructure",
            "--startup-project", "src/MigrationRunner",
            "--context", "BillingDbContext");
        addExitCode.ShouldBe(0, addOutput);
        project.Build();
    }
}

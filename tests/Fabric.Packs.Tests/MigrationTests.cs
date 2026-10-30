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
                    builder.ToTable("notes");
                    builder.HasKey(note => note.Id);
                    builder.Property(note => note.Text);
                }
            }
            """);
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

        using var postgres = Container.Postgres(DatabasePassword);
        var (runExitCode, runOutput) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        runExitCode.ShouldBe(0, runOutput);

        postgres.Psql(DatabasePassword, "select count(*) from information_schema.tables where table_schema = 'billing' and table_name = 'notes'").ShouldBe("1");
        postgres.Psql(DatabasePassword, "select \"MigrationId\" from billing.\"__EFMigrationsHistory\"").ShouldEndWith("_Initial");
    }
}

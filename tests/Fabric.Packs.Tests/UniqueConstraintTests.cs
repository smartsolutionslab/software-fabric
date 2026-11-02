using System.Net;
using System.Text.Json;

namespace Fabric.Packs.Tests;

/// <summary>
/// backend.data-access.ef-core at the HTTP edge: a write a unique index refuses, because another writer got past the
/// application's own check first, is a conflict and answers 409; every other database failure stays a 500.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class UniqueConstraintTests
{
    private const string Profile = "lean-crud";
    private const string DatabasePassword = "fabric-tests";

    private const string Note = """
        namespace Shop.Modules.Billing.Domain;

        public sealed class Note
        {
            public Guid Id { get; set; }

            public string Text { get; set; } = string.Empty;
        }
        """;

    private const string NoteConfiguration = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Shop.Modules.Billing.Domain;

        namespace Shop.Modules.Billing.Infrastructure.Persistence.Configurations;

        public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
        {
            public void Configure(EntityTypeBuilder<Note> builder)
            {
                builder.ToTable("notes");
                builder.HasKey(note => note.Id);
                builder.Property(note => note.Id).HasColumnName("id");
                builder.Property(note => note.Text).HasColumnName("text").HasMaxLength(10);
                builder.HasIndex(note => note.Text).IsUnique();
            }
        }
        """;

    /// <summary>Inserts without checking first, as the loser of a race past an application-level check would.</summary>
    private const string NotesEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.Modules.Billing.Domain;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Api;

        public sealed class Notes : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapPost("/notes/{text}", async (string text, BillingDbContext context, CancellationToken cancellationToken) =>
                {
                    context.Add(new Note { Id = Guid.CreateVersion7(), Text = text });
                    await context.SaveChangesAsync(cancellationToken);
                    return Results.Created();
                }).AllowAnonymous();
        }
        """;

    [Fact]
    public async Task The_same_unique_key_written_twice_answers_409_problem_details_and_any_other_database_failure_500()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Note.cs", Note);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/Configurations/NoteConfiguration.cs", NoteConfiguration);
        project.Write("src/Modules/Billing/Billing.Api/Notes.cs", NotesEndpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var connectionString = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}";
        var (migrateExitCode, migrateOutput) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = connectionString },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        migrateExitCode.ShouldBe(0, migrateOutput);
        postgres.Psql(DatabasePassword, "create table billing.notes (id uuid primary key, text varchar(10) not null constraint ux_notes_text unique)");

        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = connectionString,
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        using var first = await host.PostAsync("/billing/notes/paid");
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var second = await host.PostAsync("/billing/notes/paid");
        var conflict = await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict, conflict);
        second.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", conflict);
        using (var problem = JsonDocument.Parse(conflict))
        {
            problem.RootElement.GetProperty("status").GetInt32().ShouldBe(409, conflict);
            problem.RootElement.GetProperty("code").GetString().ShouldBe("already-exists", conflict);
        }

        // Nothing about the index or the colliding value reaches the caller.
        conflict.ShouldNotContain("ux_notes_text");
        conflict.ShouldNotContain("paid");

        // Longer than the column allows: a database failure, but not a unique-key collision.
        using var tooLong = await host.PostAsync("/billing/notes/far-too-long-for-the-column");
        var failure = await tooLong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, failure);
        failure.ShouldNotContain("already-exists");
    }
}

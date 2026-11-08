using System.Globalization;
using System.Net;
using System.Text.Json;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// backend.database.sqlserver: SQL Server from Aspire, EF Core's SQL Server provider, and what other packs keep in the
/// database (the in-process outbox of transport none, Wolverine's envelopes, the unique-constraint responses) on it.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class SqlServerTests
{
    private const string SqlServer = "backend.database=sqlserver";
    private const string DatabasePassword = "Fabric-tests-1";

    /// <summary>The profiles whose rules admit SQL Server: the others choose Marten, rag or dashboards, which need PostgreSQL.</summary>
    public static TheoryData<string> Profiles => ["lean-crud", "saas-standard-react", "saas-standard-vue", "saas-strict-react"];

    private static Dictionary<string, string> Dependencies(Container sqlServer) => new()
    {
        ["ConnectionStrings__database"] = $"Server=127.0.0.1,{sqlServer.Port};Database=shop;User ID=sa;Password={DatabasePassword};TrustServerCertificate=True",
        ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
    };

    /// <summary>What Aspire does before it starts the host: every module's migrations, then the runner's further steps.</summary>
    private static void RunMigrationRunner(GeneratedProject project, Container sqlServer)
    {
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = Dependencies(sqlServer)["ConnectionStrings__database"] },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void Sql_server_has_a_pack()
    {
        Repository.Packs.Packs.ShouldContainKey("backend.database.sqlserver");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_profile_that_admits_sql_server_generates_it_from_aspire_without_postgresql(string profile)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, [SqlServer]);
        generated.Errors.ShouldBeEmpty();

        var code = generated.Files
            .Where(file => !file.Path.StartsWith("docs/", StringComparison.Ordinal) && !file.Path.EndsWith(".md", StringComparison.Ordinal))
            .ToList();
        var postgres = code
            .Where(file => file.Content.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) || file.Content.Contains("Postgres", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.Path)
            .ToList();
        postgres.ShouldBeEmpty();
        code.Where(file => file.Content.Contains("#error", StringComparison.Ordinal)).Select(file => file.Path).ShouldBeEmpty();

        var appHost = generated.Files.Single(file => file.Path == "src/AppHost/AppHost.cs").Content;
        appHost.ShouldContain("builder.AddSqlServer(\"sqlserver\")");
        appHost.ShouldContain(".AddDatabase(\"database\")");
        var versions = generated.Files.Single(file => file.Path == "Directory.Packages.props").Content;
        versions.ShouldContain("<PackageVersion Include=\"Aspire.Hosting.SqlServer\"");
        versions.ShouldContain("<PackageVersion Include=\"Microsoft.EntityFrameworkCore.SqlServer\"");

        var agents = generated.Files.Single(file => file.Path == "AGENTS.md").Content;
        agents.ShouldContain("## Database: SQL Server");
        agents.ShouldNotContain("## Database: PostgreSQL");
    }

    private const string InvoicePaid = """
        namespace Shop.Contracts.Billing;

        public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
        """;

    private const string PayInvoice = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.Contracts.Billing;
        using Shop.Modules.Billing.Application;

        namespace Shop.Modules.Billing.Api;

        public sealed class PayInvoice : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapPost("/invoices/{id}/pay", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                {
                    await unitOfWork.PublishAsync(new InvoicePaidV1(id));
                    await unitOfWork.CommitAsync(cancellationToken);
                    return Results.Accepted();
                }).AllowAnonymous();
        }
        """;

    [Fact]
    public async Task The_migration_runner_creates_every_modules_outbox_table_and_an_integration_event_crosses_two_modules()
    {
        using var project = new GeneratedProject("lean-crud", SqlServer);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", InvoicePaid);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", PayInvoice);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", RedeliveredEvent.Consumer(tenancy: false));
        project.Write("src/Modules/Shipping/Shipping.Api/Deliveries.cs", RedeliveredEvent.Endpoint);
        project.Build();

        using var sqlServer = Container.SqlServer(DatabasePassword);
        RunMigrationRunner(project, sqlServer);
        sqlServer.Sql(DatabasePassword, "select string_agg(table_schema, ',') within group (order by table_schema) from information_schema.tables where table_name = 'outbox_messages'")
            .ShouldBe("billing,shipping");
        // Run twice, as every deployment does: the outbox tables already exist.
        RunMigrationRunner(project, sqlServer);

        using var host = project.StartHost(Dependencies(sqlServer));
        (await host.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var publishedAfter = TimeProvider.System.GetUtcNow();
        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await RedeliveredEvent.ShouldHaveBeenDeliveredTwiceWithTheSameMetadataAsync(host, publishedAfter, null, attempts: 60);
        sqlServer.Sql(DatabasePassword, "select count(*) from billing.outbox_messages where delivered_at is not null").ShouldBe("1");
    }

    /// <summary>
    /// With READ_COMMITTED_SNAPSHOT on, as Azure SQL Database has it by default, read committed reads row versions and
    /// takes no shared locks; the dispatcher's dequeue must still lock the rows it takes and skip the ones it cannot.
    /// </summary>
    [Fact]
    public async Task An_integration_event_crosses_two_modules_on_a_database_with_read_committed_snapshot()
    {
        using var project = new GeneratedProject("lean-crud", SqlServer);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", InvoicePaid);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", PayInvoice);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", RedeliveredEvent.Consumer(tenancy: false));
        project.Write("src/Modules/Shipping/Shipping.Api/Deliveries.cs", RedeliveredEvent.Endpoint);
        project.Build();

        using var sqlServer = Container.SqlServer(DatabasePassword);
        RunMigrationRunner(project, sqlServer);
        sqlServer.Sql(DatabasePassword, "alter database shop set read_committed_snapshot on with rollback immediate", "master");
        sqlServer.Sql(DatabasePassword, "select is_read_committed_snapshot_on from sys.databases where name = 'shop'", "master").ShouldBe("1");

        using var host = project.StartHost(Dependencies(sqlServer));
        var publishedAfter = TimeProvider.System.GetUtcNow();
        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await RedeliveredEvent.ShouldHaveBeenDeliveredTwiceWithTheSameMetadataAsync(host, publishedAfter, null, attempts: 60);
        sqlServer.Sql(DatabasePassword, "select count(*) from billing.outbox_messages where delivered_at is not null").ShouldBe("1");
    }

    [Fact]
    public async Task A_failing_consumer_waits_its_doubling_backoff_on_sql_server()
    {
        using var project = new GeneratedProject("lean-crud", SqlServer);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoiceVoidedV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoiceVoidedV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/VoidInvoice.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Contracts.Billing;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api;

            public sealed class VoidInvoice : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapPost("/invoices/{id}/void", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        await unitOfWork.PublishAsync(new InvoiceVoidedV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoiceVoidedConsumer.cs", """
            using System.Collections.Concurrent;
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoiceVoidedConsumer
            {
                /// <summary>When each attempt started, in Unix milliseconds.</summary>
                public static ConcurrentQueue<long> Attempts { get; } = [];

                public static void Handle(InvoiceVoidedV1 message)
                {
                    Attempts.Enqueue(TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds());
                    throw new InvalidOperationException($"The carrier is down; {message.InvoiceId} cannot be cancelled yet.");
                }
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Api/VoidedAttempts.cs", """
            using System.Globalization;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Shipping.Application;

            namespace Shop.Modules.Shipping.Api;

            public sealed class VoidedAttempts : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/voided-attempts", () => string.Join(',', InvoiceVoidedConsumer.Attempts.Select(attempt => attempt.ToString(CultureInfo.InvariantCulture)))).AllowAnonymous();
            }
            """);
        project.Build();

        using var sqlServer = Container.SqlServer(DatabasePassword);
        RunMigrationRunner(project, sqlServer);
        using var host = project.StartHost(Dependencies(sqlServer));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/void")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        long[] attempts = [];
        for (var poll = 0; poll < 30 && attempts.Length < 3; poll++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            var recorded = await (await host.GetAsync("/shipping/voided-attempts")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            attempts = [.. recorded.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(attempt => long.Parse(attempt, CultureInfo.InvariantCulture))];
        }

        attempts.Length.ShouldBeGreaterThanOrEqualTo(3);
        for (var retry = 1; retry < attempts.Length; retry++)
        {
            (attempts[retry] - attempts[retry - 1]).ShouldBeGreaterThanOrEqualTo(1000L << (retry - 1), $"attempt {retry + 1} came too early");
        }

        // The row agrees: its next attempt waits the doubled delay after the last failure it recorded, and keeps the error.
        var row = sqlServer.Sql(
            DatabasePassword,
            "select concat(attempts, ',', datediff_big(millisecond, '1970-01-01T00:00:00Z', next_attempt_at), ',', last_error) from billing.outbox_messages")
            .Split(',', 3);
        var failures = int.Parse(row[0], CultureInfo.InvariantCulture);
        var nextAttempt = long.Parse(row[1], CultureInfo.InvariantCulture);
        failures.ShouldBeLessThan(10, "the message must not have been dead-lettered");
        nextAttempt.ShouldBeGreaterThanOrEqualTo(attempts[failures - 1] + (1000L << (failures - 1)));
        row[2].ShouldContain("The carrier is down");
    }

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
    public async Task The_same_unique_key_written_twice_on_sql_server_answers_409_and_any_other_database_failure_500()
    {
        using var project = new GeneratedProject("lean-crud", SqlServer);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Note.cs", Note);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/Configurations/NoteConfiguration.cs", NoteConfiguration);
        project.Write("src/Modules/Billing/Billing.Api/Notes.cs", NotesEndpoint);
        project.Build();

        using var sqlServer = Container.SqlServer(DatabasePassword);
        RunMigrationRunner(project, sqlServer);
        // A unique constraint (error 2627) on the column, beside the unique index the model declares (error 2601).
        sqlServer.Sql(DatabasePassword, "create table billing.notes (id uniqueidentifier primary key, text nvarchar(10) not null constraint ux_notes_text unique)");

        using var host = project.StartHost(Dependencies(sqlServer));

        using var first = await host.PostAsync("/billing/notes/paid");
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var second = await host.PostAsync("/billing/notes/paid");
        var conflict = await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict, conflict);
        using (var problem = JsonDocument.Parse(conflict))
        {
            problem.RootElement.GetProperty("code").GetString().ShouldBe("already-exists", conflict);
        }

        conflict.ShouldNotContain("ux_notes_text");
        conflict.ShouldNotContain("paid");

        // A unique index alone, without a constraint: SQL Server reports 2601 rather than 2627.
        sqlServer.Sql(DatabasePassword, "alter table billing.notes drop constraint ux_notes_text; create unique index ix_notes_text on billing.notes (text)");
        using var indexed = await host.PostAsync("/billing/notes/paid");
        var indexConflict = await indexed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        indexed.StatusCode.ShouldBe(HttpStatusCode.Conflict, indexConflict);

        using var tooLong = await host.PostAsync("/billing/notes/far-too-long-for-the-column");
        var failure = await tooLong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, failure);
        failure.ShouldNotContain("already-exists");
    }

    [Fact]
    public void The_generated_integration_tests_pass_against_sql_server_in_the_running_aspire_stack()
    {
        using var project = new GeneratedProject("lean-crud", SqlServer);
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }
}

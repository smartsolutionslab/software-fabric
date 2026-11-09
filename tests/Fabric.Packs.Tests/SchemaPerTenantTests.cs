using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.tenancy: schema-per-tenant</c> (#253): every module keeps each tenant's rows in a schema of its own,
/// <c>&lt;module&gt;_&lt;tenant&gt;</c>, for the tenants in <c>Tenancy:Tenants</c>, which the AppHost sets. The
/// MigrationRunner creates and migrates those schemas at startup, each with its own migrations history, after checking
/// each module's model against its snapshot in the module's own schema; the host never creates one. A tenant not in the
/// list, like no tenant, works in the module's own schema, where it reads nothing and cannot save.
/// </summary>
[Collection(DockerPorts.Name)]
public sealed class SchemaPerTenantTests
{
    private const string Profile = "saas-standard-react";
    private const string SchemaPerTenant = "backend.tenancy=schema-per-tenant";
    private const string DatabasePassword = "fabric-tests";

    [Fact]
    public void Every_module_context_works_in_its_tenants_schema_and_the_apphost_lists_the_tenants_for_the_runner_and_the_host()
    {
        var files = Files(Profile, SchemaPerTenant);

        files["src/BuildingBlocks/Persistence/ModuleDbContext.cs"].ShouldContain("public string CurrentSchema => currentSchema ??= Choose(ModuleSchema, Services);");
        files["src/BuildingBlocks/Persistence/ModuleDbContext.cs"].ShouldContain("private static string Choose(string schema, IServiceProvider services) => Shop.BuildingBlocks.Tenancy.TenantSchemas.Of(schema, services);");
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
    public void The_in_process_outbox_refuses_a_tenancy_it_has_no_table_for_instead_of_writing_where_none_is()
    {
        // database-per-tenant, which has no pack yet, is the tenancy that still reaches the refusal.
        using var standIns = new StandIns();
        string[] overrides = ["backend.tenancy=database-per-tenant"];
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, "lean-crud", overrides);

        var generated = Generator.New(Repository.Catalog, packs, "shop", "lean-crud", overrides);

        generated.Errors.ShouldBeEmpty();
        generated.Files.Single(file => file.Path == "src/BuildingBlocks/Messaging/Outbox.cs").Content
            .ShouldStartWith("#error The in-process outbox of backend.bus.transport.none keeps its table in each module's schema, or in each listed tenant's schema of it, so it supports backend.tenancy none, column and schema-per-tenant only so far (docs/adr/0006 of the generator): with backend.tenancy database-per-tenant a module's integration events would go to a tenant's database, which has no outbox table and which nothing delivers from.");
    }

    [Fact]
    public async Task With_transport_none_an_event_published_under_a_tenant_is_delivered_from_that_tenants_outbox_in_that_tenant_alone()
    {
        using var project = new GeneratedProject("lean-crud", SchemaPerTenant);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.BuildingBlocks.Tenancy;
            using Shop.Contracts.Billing;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api;

            public sealed class PayInvoice : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapPost("/tenants/{tenant}/invoices/{id}/pay", async (string tenant, Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        using var scope = TenantScope.Begin(tenant);
                        await unitOfWork.PublishAsync(new InvoicePaidV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using System.Collections.Concurrent;
            using Shop.BuildingBlocks.Tenancy;
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                /// <summary>Every delivery: the invoice, the tenant its metadata carries, and the tenant the consumer works in.</summary>
                public static ConcurrentQueue<string> Deliveries { get; } = [];

                public static void Handle(InvoicePaidV1 message, ITenantContext tenant) =>
                    Deliveries.Enqueue($"{message.InvoiceId:D} {message.Metadata.TenantId} {tenant.TenantId}");
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Api/Deliveries.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Shipping.Application;

            namespace Shop.Modules.Shipping.Api;

            public sealed class Deliveries : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/deliveries", () => InvoicePaidConsumer.Deliveries.ToArray()).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);
        exitCode.ShouldBe(0, output);

        // Each listed tenant's schema of a module has its outbox; the module's own schema, which holds no tenant's rows, has none.
        postgres.Psql(DatabasePassword, "select string_agg(table_schema, ',' order by table_schema) from information_schema.tables where table_name = 'outbox_messages'")
            .ShouldBe("billing_tenant_a,billing_tenant_b,shipping_tenant_a,shipping_tenant_b");

        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = ConnectionString(postgres),
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
            ["Tenancy__Tenants__0"] = "tenant-a",
            ["Tenancy__Tenants__1"] = "tenant-b",
        });

        var (invoiceA, invoiceB) = (Guid.NewGuid(), Guid.NewGuid());
        (await host.PostAsync($"/billing/tenants/tenant-a/invoices/{invoiceA}/pay")).StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        (await host.PostAsync($"/billing/tenants/tenant-b/invoices/{invoiceB}/pay")).StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);

        // A tenant the list does not name has no outbox, as it has no rows: its event is refused, not left undelivered.
        (await host.PostAsync($"/billing/tenants/tenant-z/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(System.Net.HttpStatusCode.InternalServerError);

        var deliveries = "[]";
        for (var attempt = 0; attempt < 60 && System.Text.Json.JsonSerializer.Deserialize<string[]>(deliveries)!.Length < 2; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            deliveries = await (await host.GetAsync("/shipping/deliveries")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        // Give a stray third delivery, or a second of either, the time to arrive before checking there is none.
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        deliveries = await (await host.GetAsync("/shipping/deliveries")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        System.Text.Json.JsonSerializer.Deserialize<string[]>(deliveries).ShouldNotBeNull().Order(StringComparer.Ordinal).ShouldBe(
            ((string[])[$"{invoiceA:D} tenant-a tenant-a", $"{invoiceB:D} tenant-b tenant-b"]).Order(StringComparer.Ordinal),
            deliveries);

        postgres.Psql(DatabasePassword, "select count(*) from billing_tenant_a.outbox_messages where delivered_at is not null").ShouldBe("1");
        postgres.Psql(DatabasePassword, "select count(*) from billing_tenant_b.outbox_messages where delivered_at is not null").ShouldBe("1");
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
    public void Seed_data_through_HasData_is_refused_when_its_migration_is_made_and_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteNoteConfiguration(project, """builder.HasData(new Note(Guid.Parse("0190f7a0-0000-7000-8000-000000000001"), "seeded"));""");
        project.Build();

        ShouldRefuse(project, "Seed", "seed data (HasData)");
    }

    [Fact]
    public void A_quoted_reference_to_the_modules_schema_in_the_model_is_refused_when_its_migration_is_made_and_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteNoteConfiguration(project, """builder.Property(note => note.Text).HasDefaultValueSql("nextval('\"billing\".\"ids\"'::regclass)::text");""");
        project.Build();

        ShouldRefuse(project, "Default", "\"billing\".");
    }

    [Fact]
    public void A_quoted_reference_to_the_modules_schema_in_a_migration_is_refused_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Migrations/20990101000000_Numbers.cs", """
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Microsoft.EntityFrameworkCore.Migrations;

            namespace Shop.Modules.Billing.Infrastructure.Migrations;

            [DbContext(typeof(Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext))]
            [Migration("20990101000000_Numbers")]
            public sealed class Numbers : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                    Ensure.That(migrationBuilder).IsNotNull();

                    migrationBuilder.EnsureSchema(name: "billing");
                    migrationBuilder.CreateSequence(name: "ids", schema: "billing");
                    migrationBuilder.AddColumn<long>(
                        name: "Number",
                        schema: "billing",
                        table: "notes",
                        nullable: false,
                        defaultValueSql: "nextval('\"billing\".\"ids\"'::regclass)");
                }

                protected override void Down(MigrationBuilder migrationBuilder)
                {
                    Ensure.That(migrationBuilder).IsNotNull();

                    migrationBuilder.DropColumn(name: "Number", schema: "billing", table: "notes");
                    migrationBuilder.DropSequence(name: "ids", schema: "billing");
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("20990101000000_Numbers");
        output.ShouldContain("\"billing\".");
        NothingWasMigrated(postgres);
    }

    [Fact]
    public void HiLo_and_its_sequence_are_refused_when_their_migration_is_made_and_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        project.Write("src/Modules/Billing/Billing.Domain/Counter.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Counter(int id)
            {
                public int Id { get; private set; } = id;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/CounterConfiguration.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            public sealed class CounterConfiguration : IEntityTypeConfiguration<Counter>
            {
                public void Configure(EntityTypeBuilder<Counter> builder)
                {
                    builder.ToTable("counters");
                    builder.HasKey(counter => counter.Id);
                    builder.Property(counter => counter.Id).UseHiLo("ids", BillingDbContext.Schema);
                }
            }
            """);
        project.Build();

        ShouldRefuse(project, "Counters", "sequence billing.ids");
    }

    [Fact]
    public void Entity_splitting_is_refused_when_its_migration_is_made_and_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteNoteConfiguration(project, """builder.SplitToTable("note_details", BillingDbContext.Schema, table => table.Property(note => note.Text));""");
        project.Build();

        ShouldRefuse(project, "Split", "entity splitting (SplitToTable or SplitToView) of Note");
    }

    [Fact]
    public void A_string_literal_that_reads_like_the_modules_schema_is_no_reference_to_it_and_migrates_into_every_tenants_schema()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteNoteConfiguration(project, """builder.Property(note => note.Text).HasDefaultValueSql("'billing.example'");""");
        project.Build();

        var (addExitCode, addOutput) = AddMigration(project, "Example");
        addExitCode.ShouldBe(0, addOutput);
        project.Build();
        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldBe(0, output);
        foreach (var schema in (string[])["billing_tenant_a", "billing_tenant_b"])
        {
            postgres.Psql(DatabasePassword, $"select column_default from information_schema.columns where table_schema = '{schema}' and table_name = 'notes' and column_name = 'Text'").ShouldStartWith("'billing.example'", customMessage: schema);
        }
    }

    [Fact]
    public void A_reference_to_the_modules_schema_split_by_a_comment_and_whitespace_is_refused_when_its_migration_is_made_and_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteNoteConfiguration(project, """builder.Property(note => note.Text).HasDefaultValueSql("billing /* c */ . shared_value()");""");
        project.Build();

        ShouldRefuse(project, "Comment", "billing /* c */ . shared_value()");
    }

    [Fact]
    public void A_provider_annotation_naming_the_modules_schema_is_refused_when_its_migration_is_made_and_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/BillingDbContext.cs", """
            using Microsoft.EntityFrameworkCore;
            using Shop.BuildingBlocks.Persistence;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, IServiceProvider services)
                : ModuleDbContext(options, services, Schema)
            {
                /// <summary>The module's tables and its migrations history live in this schema.</summary>
                public const string Schema = "billing";

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    Ensure.That(modelBuilder).IsNotNull();

                    base.OnModelCreating(modelBuilder);
                    modelBuilder.HasPostgresEnum(Schema, "mood", ["happy", "sad"]);
                }
            }
            """);
        project.Build();

        ShouldRefuse(project, "Mood", "Npgsql:Enum:billing.mood");
    }

    [Fact]
    public void A_module_that_cannot_move_fails_the_runner_before_any_other_module_migrates()
    {
        // Billing, a valid module with its migration, comes before zeta, whose seed data is refused.
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        project.AddModule("zeta");
        MigrationTests.AddNotesWithAMigration(project);
        project.Write("src/Modules/Zeta/Zeta.Domain/Tally.cs", """
            namespace Shop.Modules.Zeta.Domain;

            public sealed class Tally(int id)
            {
                public int Id { get; private set; } = id;
            }
            """);
        project.Write("src/Modules/Zeta/Zeta.Infrastructure/Persistence/TallyConfiguration.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.Zeta.Domain;

            namespace Shop.Modules.Zeta.Infrastructure.Persistence;

            public sealed class TallyConfiguration : IEntityTypeConfiguration<Tally>
            {
                public void Configure(EntityTypeBuilder<Tally> builder)
                {
                    builder.ToTable("tallies");
                    builder.HasKey(tally => tally.Id);
                    builder.HasData(new Tally(1));
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("seed data (HasData) on Tally");
        NothingWasMigrated(postgres);
    }

    [Theory]
    [InlineData("""modelBuilder.Entity<Note>().ToTable("notes", Schema);""")]
    [InlineData("modelBuilder.HasDefaultSchema(Schema);")]
    public void A_table_a_contexts_own_OnModelCreating_maps_to_the_modules_schema_after_the_base_moves_to_each_tenants_schema(string statement)
    {
        // #350, the round-4 review of #334: a derived context's ToTable("notes", Schema), after base.OnModelCreating,
        // kept every tenant's rows in billing.notes; so would its HasDefaultSchema(Schema). The probe stands in for
        // mantine and writes a note for each tenant once the runner has migrated their schemas.
        using var probe = new ProbePack("frontend.ui-library.mantine", new Dictionary<string, string>
        {
            ["migration-runner.steps"] = """
                foreach (var probeTenant in (string[])["tenant-a", "tenant-b"])
                {
                    using var probeTenantScope = Shop.BuildingBlocks.Tenancy.TenantScope.Begin(probeTenant);
                    await using var probeScope = host.Services.CreateAsyncScope();
                    var billing = probeScope.ServiceProvider.GetRequiredService<Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext>();
                    billing.Add(new Shop.Modules.Billing.Domain.Note(Guid.CreateVersion7(), probeTenant));
                    await billing.SaveChangesAsync();
                }
                """,
        });
        using var project = new GeneratedProject(Profile, [probe.Manifest], SchemaPerTenant, "frontend.ui-library=mantine");
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteBillingContext(project, statement);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldBe(0, output);
        postgres.Psql(DatabasePassword, "select \"Text\" from billing_tenant_a.notes").ShouldBe("tenant-a");
        postgres.Psql(DatabasePassword, "select \"Text\" from billing_tenant_b.notes").ShouldBe("tenant-b");
        postgres.Psql(DatabasePassword, "select count(*) from billing.notes").ShouldBe("0");
    }

    [Theory]
    [InlineData("""builder.Property(note => note.Text).HasDefaultValue("billing.example");""", "select column_default from information_schema.columns where table_schema = '{0}' and table_name = 'notes' and column_name = 'Text'", "'billing.example'")]
    [InlineData("""builder.Property(note => note.Text).HasComment("Copied from billing.notes");""", "select col_description(attrelid, attnum) from pg_attribute where attrelid = '{0}.notes'::regclass and attname = 'Text'", "Copied from billing.notes")]
    public void A_literal_value_that_reads_like_the_modules_schema_is_data_and_migrates_into_every_tenants_schema(string statement, string query, string expected)
    {
        // #350, the round-4 review of #334: HasDefaultValue("billing.example") was read as SQL naming billing, in the
        // model and in the migration's ColumnOperation.DefaultValue, so a valid literal could not be migrated.
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteNoteConfiguration(project, statement);
        project.Build();

        var (addExitCode, addOutput) = AddMigration(project, "Literal");
        addExitCode.ShouldBe(0, addOutput);
        project.Build();
        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldBe(0, output);
        foreach (var schema in (string[])["billing_tenant_a", "billing_tenant_b"])
        {
            postgres.Psql(DatabasePassword, string.Format(System.Globalization.CultureInfo.InvariantCulture, query, schema)).ShouldStartWith(expected, customMessage: schema);
        }
    }

    [Theory]
    [InlineData("nextval(CAST('billing.ids' AS pg_catalog.regclass))::text")]
    [InlineData("pg_sequence_last_value('billing.ids'::pg_catalog.regclass)::text")]
    [InlineData("pg_sequence_last_value('billing.ids'::\"pg_catalog\" . \"regclass\")::text")]
    public void A_reference_cast_to_a_schema_qualified_object_identifier_type_is_refused_when_its_migration_is_made_and_before_the_runner_migrates_anything(string sql)
    {
        // #350, the review of #334 at 468a4aa: the type after AS or :: was pg_catalog, not a reg* type, so the reference
        // passed both checks and every tenant shared billing.ids.
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        WriteNoteConfiguration(project, $$"""builder.Property(note => note.Text).HasDefaultValueSql("{{sql.Replace("\"", "\\\"", StringComparison.Ordinal)}}");""");
        project.Build();

        ShouldRefuse(project, "Cast", sql);
    }

    [Fact]
    public void A_reference_cast_to_a_schema_qualified_object_identifier_type_in_a_migration_is_refused_before_the_runner_migrates_anything()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Migrations/20990101000000_Numbers.cs", """
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Microsoft.EntityFrameworkCore.Migrations;

            namespace Shop.Modules.Billing.Infrastructure.Migrations;

            [DbContext(typeof(Shop.Modules.Billing.Infrastructure.Persistence.BillingDbContext))]
            [Migration("20990101000000_Numbers")]
            public sealed class Numbers : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                    Ensure.That(migrationBuilder).IsNotNull();

                    migrationBuilder.AddColumn<long>(
                        name: "Number",
                        schema: "billing",
                        table: "notes",
                        nullable: false,
                        defaultValueSql: "nextval(CAST('billing.ids' AS pg_catalog.regclass))");
                }

                protected override void Down(MigrationBuilder migrationBuilder)
                {
                    Ensure.That(migrationBuilder).IsNotNull();

                    migrationBuilder.DropColumn(name: "Number", schema: "billing", table: "notes");
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("20990101000000_Numbers");
        output.ShouldContain("CAST('billing.ids' AS pg_catalog.regclass)");
        NothingWasMigrated(postgres);
    }

    [Fact]
    public void A_many_to_many_relationship_with_an_implicit_join_entity_builds_its_model_and_migrates_into_every_tenants_schema()
    {
        // #350, the review of #334 at fa40669: the query filter of the module's own schema registered EF's shared-type
        // join entity (Dictionary<string, object>) by its CLR type, which threw while the model was built.
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        MigrationTests.AddNotesWithAMigration(project);
        project.Write("src/Modules/Billing/Billing.Domain/Note.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain;

            public sealed class Note(Guid id, string text) : Entity<Guid>(id)
            {
                public string Text { get; private set; } = text;

                public ICollection<Tag> Tags { get; } = [];
            }
            """);
        project.Write("src/Modules/Billing/Billing.Domain/Tag.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain;

            public sealed class Tag(Guid id, string name) : Entity<Guid>(id)
            {
                public string Name { get; private set; } = name;

                public ICollection<Note> Notes { get; } = [];
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/TagConfiguration.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
            {
                public void Configure(EntityTypeBuilder<Tag> builder)
                {
                    builder.ToTable("tags");
                    builder.HasKey(tag => tag.Id);
                    builder.Property(tag => tag.Name);
                }
            }
            """);
        WriteNoteConfiguration(project, "builder.HasMany(note => note.Tags).WithMany(tag => tag.Notes);");
        project.Build();

        var (addExitCode, addOutput) = AddMigration(project, "Tags");
        addExitCode.ShouldBe(0, addOutput);
        project.Build();
        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldBe(0, output);
        foreach (var schema in (string[])["billing_tenant_a", "billing_tenant_b"])
        {
            postgres.Psql(DatabasePassword, $"select count(*) from information_schema.tables where table_schema = '{schema}' and table_name = 'NoteTag'").ShouldBe("1", schema);
        }
    }

    [Fact]
    public void The_generated_tenancy_tests_show_no_tenant_reads_another_tenants_rows_and_the_architecture_rules_pass()
    {
        using var project = new GeneratedProject(Profile, SchemaPerTenant);
        project.AddModule("billing");
        project.Build();

        var (tenancyExitCode, tenancyOutput) = project.Test("tests/Tenancy.Tests/Tenancy.Tests.csproj");
        tenancyExitCode.ShouldBe(0, tenancyOutput);
        tenancyOutput.ShouldContain("succeeded: 11");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
    }

    /// <summary>The billing notes' configuration with one more statement on the builder.</summary>
    private static void WriteNoteConfiguration(GeneratedProject project, string statement) =>
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/NoteConfiguration.cs", $$"""
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
                    {{statement}}
                }
            }
            """);

    /// <summary>The billing context with one more statement after the base's OnModelCreating.</summary>
    private static void WriteBillingContext(GeneratedProject project, string statement) =>
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/BillingDbContext.cs", $$"""
            using Microsoft.EntityFrameworkCore;
            using Shop.BuildingBlocks.Persistence;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, IServiceProvider services)
                : ModuleDbContext(options, services, Schema)
            {
                /// <summary>The module's tables and its migrations history live in this schema.</summary>
                public const string Schema = "billing";

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    Ensure.That(modelBuilder).IsNotNull();

                    base.OnModelCreating(modelBuilder);
                    {{statement}}
                }
            }
            """);

    /// <summary>
    /// Making a migration of the model fails naming what cannot live in a tenant's schema, and so does the MigrationRunner,
    /// before it migrates any schema.
    /// </summary>
    private static void ShouldRefuse(GeneratedProject project, string migration, string named)
    {
        var (addExitCode, addOutput) = AddMigration(project, migration);
        addExitCode.ShouldNotBe(0, addOutput);
        addOutput.ShouldContain(named);

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = RunMigrationRunner(project, postgres);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain(named);
        NothingWasMigrated(postgres);
    }

    private static (int ExitCode, string Output) AddMigration(GeneratedProject project, string migration) =>
        project.DotNetCommand(
            new Dictionary<string, string>(),
            "ef", "migrations", "add", migration,
            "--project", "src/Modules/Billing/Billing.Infrastructure",
            "--startup-project", "src/MigrationRunner",
            "--context", "BillingDbContext");

    /// <summary>No migration ran: EF Core may have created the module's history table, but recorded nothing in it.</summary>
    private static void NothingWasMigrated(Container postgres)
    {
        postgres.Psql(DatabasePassword, "select count(*) from information_schema.schemata where schema_name like 'billing\\_%'").ShouldBe("0");
        postgres.Psql(DatabasePassword, "select count(*) from information_schema.tables where table_schema = 'billing' and table_name = 'notes'").ShouldBe("0");
    }

    private static string ConnectionString(Container postgres) =>
        $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}";

    private static (int ExitCode, string Output) RunMigrationRunner(GeneratedProject project, Container postgres) =>
        project.DotNetCommand(
            new Dictionary<string, string>
            {
                ["ConnectionStrings__database"] = ConnectionString(postgres),
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

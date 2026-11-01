using System.Globalization;
using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>
/// lean-crud's backend from the real packs: no broker, no tenancy, CRUD modules without CQRS. Its frontend packs are
/// M2-02b; until then the project uses saas-standard-react's frontend so it generates and builds.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class LeanCrudBackendTests
{
    private const string Profile = "lean-crud";
    private const string DatabasePassword = "fabric-tests";

    private static readonly string[] Frontend = ["frontend.state=redux-toolkit", "frontend.api-client=rtk-query-codegen"];

    private static readonly string[] BackendOptions = ["backend.bus.transport.none", "backend.tenancy.none", "module.style.crud", "module.cqrs.none"];

    private static Dictionary<string, string> Dependencies(Container postgres) => new()
    {
        ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
    };

    /// <summary>What Aspire does before it starts the host: every module's migrations, then the runner's further steps.</summary>
    private static void RunMigrationRunner(GeneratedProject project, Container postgres)
    {
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = Dependencies(postgres)["ConnectionStrings__database"] },
            "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public async Task The_migration_runner_creates_every_modules_outbox_table_and_the_host_creates_none()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using (var host = project.StartHost(Dependencies(postgres)))
        {
            (await host.GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        postgres.Psql(DatabasePassword, "select count(*) from information_schema.tables where table_name = 'outbox_messages'").ShouldBe("0");

        RunMigrationRunner(project, postgres);

        postgres.Psql(DatabasePassword, "select string_agg(table_schema, ',' order by table_schema) from information_schema.tables where table_name = 'outbox_messages'").ShouldBe("billing,shipping");
    }

    [Fact]
    public void Every_backend_option_of_lean_crud_has_a_pack()
    {
        foreach (var option in BackendOptions)
        {
            Repository.Packs.Packs.ShouldContainKey(option);
        }
    }

    [Fact]
    public void A_lean_crud_project_builds_and_a_new_modules_tests_and_the_architecture_rules_pass()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Build();

        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        standIns.ShouldBeEmpty();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public async Task An_integration_event_crosses_two_modules_in_process_through_the_outbox_without_a_broker()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", """
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
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using System.Collections.Concurrent;
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                private readonly ConcurrentBag<Guid> received = Received;

                public static ConcurrentBag<Guid> Received { get; } = [];

                public Task Handle(InvoicePaidV1 message, CancellationToken cancellationToken)
                {
                    received.Add(message.InvoiceId);
                    return Task.CompletedTask;
                }
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Api/Received.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Shipping.Application;

            namespace Shop.Modules.Shipping.Api;

            public sealed class Received : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/received", () => InvoicePaidConsumer.Received.Count).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        using var host = project.StartHost(Dependencies(postgres));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var received = "0";
        for (var attempt = 0; attempt < 60 && received == "0"; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            received = await (await host.GetAsync("/shipping/received")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        received.ShouldBe("1");
        postgres.Psql(DatabasePassword, "select count(*) from billing.outbox_messages where delivered_at is not null").ShouldBe("1");
    }

    [Fact]
    public async Task A_failing_consumer_is_not_retried_before_its_backoff_has_passed_while_other_events_flow()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Contracts/Billing/InvoiceVoidedV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoiceVoidedV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/Publish.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Contracts.Billing;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api;

            public sealed class Publish : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapPost("/invoices/{id}/pay", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        await unitOfWork.PublishAsync(new InvoicePaidV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
                    endpoints.MapPost("/invoices/{id}/void", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        await unitOfWork.PublishAsync(new InvoiceVoidedV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
                }
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                public static void Handle(InvoicePaidV1 message) => _ = message;
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
                    Attempts.Enqueue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        using var host = project.StartHost(Dependencies(postgres));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/void")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Keep the dispatcher busy: each delivered event sends it straight round its loop again.
        for (var paid = 0; paid < 40; paid++)
        {
            (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        // The assertions compare attempts with each other, never with the clock when the test happens to look:
        // however long the requests above took, every gap between attempts must be at least its backoff.
        long[] attempts = [];
        for (var poll = 0; poll < 30 && attempts.Length < 2; poll++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            var recorded = await (await host.GetAsync("/shipping/voided-attempts")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            attempts = [.. recorded.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(attempt => long.Parse(attempt, CultureInfo.InvariantCulture))];
        }

        attempts.Length.ShouldBeGreaterThanOrEqualTo(2);
        for (var retry = 1; retry < attempts.Length; retry++)
        {
            (attempts[retry] - attempts[retry - 1]).ShouldBeGreaterThanOrEqualTo(1000L << (retry - 1), $"attempt {retry + 1} came too early");
        }

        // The row agrees: its next attempt waits the doubled delay after the last failure it recorded.
        const string Voided = "from billing.outbox_messages where type like 'Shop.Contracts.Billing.InvoiceVoidedV1,%'";
        var row = postgres.Psql(DatabasePassword, $"select attempts || ',' || (extract(epoch from next_attempt_at) * 1000)::bigint {Voided}").Split(',');
        var failures = int.Parse(row[0], CultureInfo.InvariantCulture);
        var nextAttempt = long.Parse(row[1], CultureInfo.InvariantCulture);
        failures.ShouldBeLessThan(10, "the message must not have been dead-lettered");
        nextAttempt.ShouldBeGreaterThanOrEqualTo(attempts[failures - 1] + (1000L << (failures - 1)));
    }

    [Fact]
    public void A_commit_retried_after_the_outbox_insert_failed_saves_the_changes_and_the_event()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Note.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Note
            {
                public Guid Id { get; set; }

                public string Text { get; set; } = string.Empty;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/Configurations/NoteConfiguration.cs", """
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
                    builder.Property(note => note.Text);
                }
            }
            """);
        project.Write("tests/Modules/Billing.Tests/RetriedCommitTests.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Microsoft.EntityFrameworkCore.Storage;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Shop.Contracts;
            using Shop.Modules.Billing.Api;
            using Shop.Modules.Billing.Application;
            using Shop.Modules.Billing.Domain;
            using Shop.Modules.Billing.Infrastructure.Persistence;

            namespace Shop.Modules.Billing.Tests;

            /// <summary>Serializing it fails the first time, so the first commit fails after the entity was saved and before the outbox insert.</summary>
            public sealed record NoteAddedV1(Guid NoteId) : IIntegrationEvent
            {
                private static int serializations;

            #pragma warning disable CA1065 // The fixture's whole point: the outbox insert fails once.
                public string Check => Interlocked.Increment(ref serializations) == 1 ? throw new InvalidOperationException("The outbox insert failed once.") : NoteId.ToString();
            #pragma warning restore CA1065
            }

            public class RetriedCommitTests
            {
                [Fact]
                public async Task A_commit_retried_after_a_failure_saves_the_changes_and_the_event()
                {
                    var cancellationToken = TestContext.Current.CancellationToken;
                    var services = new ServiceCollection().AddLogging();
                    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                    new BillingModule().AddServices(services, configuration);
                    await using var provider = services.BuildServiceProvider();
                    var noteId = Guid.NewGuid();

                    await using (var scope = provider.CreateAsyncScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
                        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBillingUnitOfWork>();

                        context.Add(new Note { Id = noteId, Text = "paid" });
                        await unitOfWork.PublishAsync(new NoteAddedV1(noteId));
                        await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(cancellationToken));
                        await unitOfWork.CommitAsync(cancellationToken);
                    }

                    await using (var scope = provider.CreateAsyncScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                        (await context.Set<Note>().CountAsync(note => note.Id == noteId, cancellationToken)).ShouldBe(1);
                        (await context.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from billing.outbox_messages").SingleAsync(cancellationToken)).ShouldBe(1);
                    }
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public async Task A_consumer_whose_call_timed_out_is_retried_and_the_host_keeps_running()
    {
        using var project = ConsumerProject(
            "InvoicePaidConsumer",
            """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                private static int calls;

                public static int Received => Math.Max(0, Volatile.Read(ref calls) - 1);

                // An HTTP call that timed out: the cancellation is the consumer's own, not the host stopping.
                public static void Handle(InvoicePaidV1 message)
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        throw new TaskCanceledException($"The carrier did not answer for {message.InvoiceId}.");
                    }
                }
            }
            """,
            "InvoicePaidConsumer.Received.ToString(System.Globalization.CultureInfo.InvariantCulture)");

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        using var host = project.StartHost(Dependencies(postgres));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await PollConsumerAsync(host, "1")).ShouldBe("1");
        (await host.GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);
        postgres.Psql(DatabasePassword, "select attempts from billing.outbox_messages where delivered_at is not null").ShouldBe("2");
    }

    [Fact]
    public async Task A_disposable_consumer_is_disposed_after_it_handled_a_message()
    {
        using var project = ConsumerProject(
            "InvoicePaidConsumer",
            """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer : IAsyncDisposable
            {
                private static int handled;
                private static int disposed;
                private bool open = true;

                public static string Counts => $"{Volatile.Read(ref handled)},{Volatile.Read(ref disposed)}";

                public void Handle(InvoicePaidV1 message)
                {
                    _ = message;
                    if (open)
                    {
                        Interlocked.Increment(ref handled);
                    }
                }

                public ValueTask DisposeAsync()
                {
                    open = false;
                    Interlocked.Increment(ref disposed);
                    return ValueTask.CompletedTask;
                }
            }
            """,
            "InvoicePaidConsumer.Counts");

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        using var host = project.StartHost(Dependencies(postgres));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await PollConsumerAsync(host, "1,1")).ShouldBe("1,1");
    }

    /// <summary>billing publishes InvoicePaidV1 on POST /billing/invoices/{id}/pay; shipping's consumer reports on GET /shipping/consumer.</summary>
    private static GeneratedProject ConsumerProject(string consumer, string consumerSource, string report)
    {
        var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", """
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
            """);
        project.Write($"src/Modules/Shipping/Shipping.Application/{consumer}.cs", consumerSource);
        project.Write("src/Modules/Shipping/Shipping.Api/ConsumerReport.cs", $$"""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Shipping.Application;

            namespace Shop.Modules.Shipping.Api;

            public sealed class ConsumerReport : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/consumer", () => {{report}}).AllowAnonymous();
            }
            """);
        project.Build();
        return project;
    }

    /// <summary>Reads the consumer's report until it is the expected one, or returns the last one after 30 s.</summary>
    private static async Task<string> PollConsumerAsync(RunningHost host, string expected)
    {
        var report = string.Empty;
        for (var poll = 0; poll < 60 && report != expected; poll++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            report = await (await host.GetAsync("/shipping/consumer")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        return report;
    }

    [Fact]
    public async Task The_backoff_starts_when_a_slow_consumer_fails_not_when_its_batch_started()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoiceVoidedV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoiceVoidedV1(Guid InvoiceId) : IIntegrationEvent;
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
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoiceVoidedConsumer
            {
                private static int calls;

                public static async Task Handle(InvoiceVoidedV1 message, CancellationToken cancellationToken)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        throw new InvalidOperationException($"The carrier timed out on {message.InvoiceId}.");
                    }
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        using var host = project.StartHost(Dependencies(postgres));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/void")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        const string Voided = "from billing.outbox_messages where type like 'Shop.Contracts.Billing.InvoiceVoidedV1,%'";
        var delivered = "f";
        for (var attempt = 0; attempt < 60 && delivered != "t"; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            delivered = postgres.Psql(DatabasePassword, $"select delivered_at is not null {Voided}");
        }

        delivered.ShouldBe("t");

        // The failure came at least 1.5 s after the message was created, and the first delay is 1 s.
        postgres.Psql(DatabasePassword, $"select next_attempt_at >= created_at + interval '2.5 seconds' {Voided}").ShouldBe("t");
    }

    [Fact]
    public void A_unit_of_work_refuses_to_commit_inside_a_transaction_it_did_not_open()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("tests/Modules/Billing.Tests/CallerTransactionTests.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Shop.Contracts;
            using Shop.Modules.Billing.Api;
            using Shop.Modules.Billing.Application;
            using Shop.Modules.Billing.Infrastructure.Persistence;

            namespace Shop.Modules.Billing.Tests;

            public sealed record InvoiceIssuedV1(Guid InvoiceId) : IIntegrationEvent;

            public class CallerTransactionTests
            {
                [Fact]
                public async Task Commit_inside_a_callers_transaction_is_refused()
                {
                    var cancellationToken = TestContext.Current.CancellationToken;
                    var services = new ServiceCollection().AddLogging();
                    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                    new BillingModule().AddServices(services, configuration);
                    await using var provider = services.BuildServiceProvider();

                    await using var scope = provider.CreateAsyncScope();
                    var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IBillingUnitOfWork>();
                    await unitOfWork.PublishAsync(new InvoiceIssuedV1(Guid.NewGuid()));

                    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                    var refusal = await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(cancellationToken));
                    refusal.Message.ShouldContain("opens its own transaction");
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string> { ["ConnectionStrings__database"] = Dependencies(postgres)["ConnectionStrings__database"] });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void An_async_void_consumer_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                public static async void Handle(InvoicePaidV1 message)
                {
                    await Task.Yield();
                    _ = message;
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Shipping.Application.InvoicePaidConsumer.Handle is async void; a consumer that awaits returns Task.");
    }

    [Fact]
    public void A_consumer_that_handles_something_other_than_an_integration_event_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("shipping");
        project.Write("src/Modules/Shipping/Shipping.Application/TextConsumer.cs", """
            namespace Shop.Modules.Shipping.Application;

            public sealed class TextConsumer
            {
                public static void Handle(string text) => _ = text;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Shipping.Application.TextConsumer.Handle does not handle an integration event.");
    }

    [Fact]
    public void An_entity_that_knows_ef_core_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Billing.Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
              </ItemGroup>
            </Project>
            """);
        project.Write("src/Modules/Billing/Billing.Domain/Invoice.cs", """
            using Microsoft.EntityFrameworkCore;

            namespace Shop.Modules.Billing.Domain;

            [Index(nameof(Number))]
            public sealed class Invoice
            {
                public Guid Id { get; set; }

                public string Number { get; set; } = string.Empty;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain references Microsoft.EntityFrameworkCore.Abstractions; map entities in Infrastructure.");
    }

    [Fact]
    public void A_tenant_id_in_a_project_without_tenancy_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoice.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Invoice
            {
                public Guid Id { get; set; }

                public string TenantId { get; set; } = string.Empty;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoice has a TenantId, but this project has no tenancy.");
    }

    [Fact]
    public void A_read_model_in_a_module_without_cqrs_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/InvoiceReadModel.cs", """
            namespace Shop.Modules.Billing.Application;

            public sealed record InvoiceReadModel(Guid Id, string Number);
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.InvoiceReadModel is a separate read model; without CQRS a module reads the entities it writes.");
    }

    [Fact]
    public void The_generated_integration_tests_pass_against_the_running_aspire_stack()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }
}

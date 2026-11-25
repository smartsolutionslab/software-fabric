using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>Generates saas-standard-react from the real packs (stand-ins for the rest) and checks the result.</summary>
[Collection(FixedPorts.Name)]
public sealed class SaasStandardReactTests
{
    private const string Profile = "saas-standard-react";

    /// <summary>What a module's endpoint looks like with minimal-apis and result; the module packs (M1-04) will add real ones.</summary>
    private const string ProbeEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api;

        public sealed class Probe : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/probe", () => Result<string, ApiError>.Success("pong").ToHttpResult());
        }
        """;

    /// <summary>An endpoint that publishes InvoicePaidV1 for tenant-a through the billing module's unit of work.</summary>
    internal const string PayInvoice = """
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
                endpoints.MapPost("/invoices/{id}/pay", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                {
                    using var tenant = TenantScope.Begin("tenant-a");
                    await unitOfWork.PublishAsync(new InvoicePaidV1(id));
                    await unitOfWork.CommitAsync(cancellationToken);
                    return Results.Accepted();
                }).AllowAnonymous();
        }
        """;

    private const string DatabasePassword = "fabric-tests";

    private static Dictionary<string, string> Dependencies(Container postgres, Container rabbitMq) => new()
    {
        ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        ["ConnectionStrings__messaging"] = $"amqp://guest:guest@127.0.0.1:{rabbitMq.Port}",
        ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
    };

    [Fact]
    public void The_repository_packs_load_without_errors()
    {
        Repository.Packs.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generates_builds_and_still_builds_after_adding_a_module()
    {
        using var project = new GeneratedProject(Profile);
        project.Build();

        project.AddModule("billing");
        project.Build();

        File.Exists(Path.Combine(project.ProjectDirectory, "src", "Modules", "Billing", "Billing.Api", "bin", "Release", GeneratedProject.TargetFramework, "Shop.Modules.Billing.Api.dll")).ShouldBeTrue();
    }

    [Fact]
    public async Task The_host_finds_module_endpoints_serves_openapi_and_requires_a_token()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/Probe.cs", ProbeEndpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(Dependencies(postgres, rabbitMq));

        (await host.GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.GetAsync("/billing/probe")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void A_new_module_builds_and_its_tests_and_the_architecture_rules_pass()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_domain_aggregate_built_on_the_building_blocks_keeps_the_architecture_rules_green()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceId(Guid Value);

            public sealed class Invoice(InvoiceId id) : AggregateRoot<InvoiceId>(id);
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void An_aggregate_fails_with_a_domain_error_that_the_application_maps_to_an_api_error()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceId(Guid Value);

            public sealed record InvoiceAlreadyPaid(InvoiceId Invoice) : DomainError("billing.invoice-already-paid", "The invoice is already paid.");

            public static class InvoiceErrors
            {
                public static DomainError AlreadyPaid(InvoiceId invoice) => new InvoiceAlreadyPaid(invoice);
            }

            public sealed class Invoice(InvoiceId id) : AggregateRoot<InvoiceId>(id)
            {
                public bool IsPaid { get; private set; }

                public Result<Unit, DomainError> Pay()
                {
                    if (IsPaid)
                    {
                        return Failure(InvoiceErrors.AlreadyPaid(Id));
                    }

                    IsPaid = true;
                    return Success(Unit.Value);
                }
            }
            """);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/PayInvoice", """
            using System.Net;
            using Shop.BuildingBlocks.Messaging;
            using Shop.Modules.Billing.Domain.Invoices;

            namespace Shop.Modules.Billing.Application.Commands.PayInvoice;

            public sealed record PayInvoice(Guid Invoice) : ICommand<Result<Unit, ApiError>>;

            public static class InvoiceFailures
            {
                public static ApiError From(DomainError error) => error switch
                {
                    InvoiceAlreadyPaid => ApiError.From(error, HttpStatusCode.Conflict),
                    _ => ApiError.From(error, HttpStatusCode.UnprocessableEntity),
                };
            }

            public sealed class PayInvoiceHandler : ICommandHandler<PayInvoice, Result<Unit, ApiError>>
            {
                public Task<Result<Unit, ApiError>> HandleAsync(PayInvoice command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();

                    var invoice = new Invoice(new InvoiceId(command.Invoice));
                    return Task.FromResult(invoice.Pay().MapError(InvoiceFailures.From));
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void Every_module_context_builds_its_model_with_an_aggregate_that_raises_domain_events()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.AddModule("shipping");
        AddAnAggregateThatRaisesEvents(project, "Billing", "Invoice", "Pay", "InvoicePaid");
        AddAnAggregateThatRaisesEvents(project, "Shipping", "Parcel", "Ship", "ParcelShipped");
        project.Build();

        foreach (var module in new[] { "Billing", "Shipping" })
        {
            var (exitCode, output) = project.Test($"tests/Modules/{module}.Tests/{module}.Tests.csproj");

            exitCode.ShouldBe(0, output);
            output.ShouldContain("succeeded: 2");
        }
    }

    [Fact]
    public void A_domain_error_that_speaks_http_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/InvoiceNotFound.cs", """
            using System.Net;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceNotFound(Guid Invoice) : ApiError("billing.invoice-not-found", "No such invoice.", HttpStatusCode.NotFound);
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceNotFound is an ApiError");
    }

    [Fact]
    public void Breaking_an_architecture_rule_fails_the_generated_tests()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/InvoicesController.cs", """
            namespace Shop.Modules.Billing.Api;

            public sealed class InvoicesController : Microsoft.AspNetCore.Mvc.ControllerBase;
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("InvoicesController is a controller");
    }

    [Fact]
    public void A_rule_violation_is_reported_through_the_chosen_assertion_library()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/InvoicesController.cs", """
            namespace Shop.Modules.Billing.Api;

            public sealed class InvoicesController : Microsoft.AspNetCore.Mvc.ControllerBase;
            """);
        project.Build();

        var (_, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        output.ShouldContain("Shouldly.ShouldAssertException");
        output.ShouldContain("No module contains an MVC controller.");
    }

    [Fact]
    public async Task An_integration_event_published_by_one_module_reaches_a_consumer_in_another()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", PayInvoice);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", RedeliveredEvent.Consumer(tenancy: true));
        project.Write("src/Modules/Shipping/Shipping.Api/Deliveries.cs", RedeliveredEvent.Endpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(Dependencies(postgres, rabbitMq));

        var publishedAfter = TimeProvider.System.GetUtcNow();
        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await RedeliveredEvent.ShouldHaveBeenDeliveredTwiceWithTheSameMetadataAsync(
            host,
            publishedAfter,
            "tenant-a",
            attempts: 60);

        // Wolverine retried the failed delivery by itself; nothing was dead-lettered on the way.
        RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword).ShouldBe(0);
    }

    /// <summary>
    /// With the hand-rolled mediator, a consumer that takes the dispatcher and its module's unit of work by constructor, as
    /// the agent rules allow, runs: Wolverine cannot build either (both take the service provider), so they come from the
    /// message's scope (#822). Before, Wolverine refused the consumer and the event went to the dead letters.
    /// </summary>
    [Fact]
    public async Task A_consumer_that_takes_the_dispatcher_and_its_modules_unit_of_work_dispatches_a_command_and_publishes()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Contracts/Shipping/ShipmentScheduledV1.cs", """
            namespace Shop.Contracts.Shipping;

            public sealed record ShipmentScheduledV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", PayInvoice);
        project.WriteTypes("src/Modules/Shipping/Shipping.Application/Commands/ScheduleShipment", """
            using System.Collections.Concurrent;
            using Shop.BuildingBlocks.Messaging;

            namespace Shop.Modules.Shipping.Application.Commands.ScheduleShipment;

            public sealed record ScheduleShipment(Guid InvoiceId) : ICommand<Result<Guid, ApiError>>;

            public sealed class ScheduleShipmentHandler : ICommandHandler<ScheduleShipment, Result<Guid, ApiError>>
            {
                public static ConcurrentBag<Guid> Scheduled { get; } = [];

                public Task<Result<Guid, ApiError>> HandleAsync(ScheduleShipment command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();

                    Scheduled.Add(command.InvoiceId);
                    return Task.FromResult(Result<Guid, ApiError>.Success(command.InvoiceId));
                }
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using Shop.BuildingBlocks.Messaging;
            using Shop.BuildingBlocks.Tenancy;
            using Shop.Contracts.Billing;
            using Shop.Contracts.Shipping;
            using Shop.Modules.Shipping.Application.Commands.ScheduleShipment;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer(IDispatcher dispatcher, IShippingUnitOfWork unitOfWork)
            {
                public async Task HandleAsync(InvoicePaidV1 message, CancellationToken cancellationToken)
                {
                    Ensure.That(message).IsNotNull();

                    using var tenant = TenantScope.Begin(message.Metadata.TenantId ?? throw new InvalidOperationException("The event carries no tenant."));
                    await dispatcher.SendAsync(new ScheduleShipment(message.InvoiceId), cancellationToken);
                    await unitOfWork.PublishAsync(new ShipmentScheduledV1(message.InvoiceId));
                    await unitOfWork.CommitAsync(cancellationToken);
                }
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Api/Shipments.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Shipping.Application.Commands.ScheduleShipment;

            namespace Shop.Modules.Shipping.Api;

            public sealed class Shipments : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/scheduled", () => ScheduleShipmentHandler.Scheduled.ToArray()).AllowAnonymous();
            }
            """);
        project.Write("src/Modules/Billing/Billing.Application/ShipmentScheduledConsumer.cs", """
            using System.Collections.Concurrent;
            using Shop.Contracts.Shipping;

            namespace Shop.Modules.Billing.Application;

            public static class ShipmentScheduledConsumer
            {
                public static ConcurrentBag<Guid> Received { get; } = [];

                public static void Handle(ShipmentScheduledV1 message)
                {
                    Ensure.That(message).IsNotNull();

                    Received.Add(message.InvoiceId);
                }
            }
            """);
        project.Write("src/Modules/Billing/Billing.Api/ScheduledShipments.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api;

            public sealed class ScheduledShipments : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/shipments/scheduled", () => ShipmentScheduledConsumer.Received.ToArray()).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(Dependencies(postgres, rabbitMq));

        var invoice = Guid.CreateVersion7();
        (await host.PostAsync($"/billing/invoices/{invoice}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // The command the consumer dispatched ran, and the event it published through its unit of work reached billing.
        var scheduled = "[]";
        var published = "[]";
        for (var attempt = 0; attempt < 120 && !(scheduled.Contains(invoice.ToString(), StringComparison.Ordinal) && published.Contains(invoice.ToString(), StringComparison.Ordinal)); attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            scheduled = await (await host.GetAsync("/shipping/scheduled")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            published = await (await host.GetAsync("/billing/shipments/scheduled")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        var deadLetters = RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword) == 0
            ? string.Empty
            : postgres.Psql(DatabasePassword, "select exception_message from wolverine.wolverine_dead_letters");
        scheduled.ShouldContain(invoice.ToString(), customMessage: $"Dead letters: {deadLetters}");
        published.ShouldContain(invoice.ToString(), customMessage: $"Dead letters: {deadLetters}");
        deadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_consumer_that_always_fails_is_retried_with_backoff_then_dead_lettered()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", PayInvoice);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using System.Collections.Concurrent;
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                private readonly ConcurrentQueue<DateTimeOffset> deliveries = Deliveries;

                public static ConcurrentQueue<DateTimeOffset> Deliveries { get; } = [];

                public void Handle(InvoicePaidV1 message)
                {
                    deliveries.Enqueue(TimeProvider.System.GetUtcNow());
                    throw new InvalidOperationException($"Delivery {deliveries.Count} of {message.InvoiceId} fails, as every one does.");
                }
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
                    endpoints.MapGet("/deliveries", () => InvoicePaidConsumer.Deliveries.Select(delivery => delivery.ToUnixTimeMilliseconds()).ToArray()).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(Dependencies(postgres, rabbitMq));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // The pack's ADR: seven attempts, the retries 1, 2, 4, 8, 16 and 32 seconds after the failure before them.
        for (var attempt = 0; attempt < 360 && RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword) == 0; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        var body = await (await host.GetAsync("/shipping/deliveries")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword).ShouldBe(1, $"Deliveries: {body}");

        var deliveries = System.Text.Json.JsonSerializer.Deserialize<long[]>(body).ShouldNotBeNull();
        deliveries.Length.ShouldBe(7, body);
        int[] backoff = [1, 2, 4, 8, 16, 32];
        for (var retry = 0; retry < backoff.Length; retry++)
        {
            (deliveries[retry + 1] - deliveries[retry]).ShouldBeGreaterThanOrEqualTo(backoff[retry] * 1000L - 100, $"Retry {retry + 1}: {body}");
        }
    }

    [Fact]
    public async Task An_event_from_the_broker_whose_consumer_always_fails_is_dead_lettered_in_the_table_the_docs_name()
    {
        // The host routes an event its own modules publish and consume to a local queue, so the test above never
        // reaches the broker. Here another service publishes it onto the broker, where Wolverine's RabbitMQ transport
        // has a dead-letter queue of its own (#684).
        using var project = new GeneratedProject(Profile);
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                public static void Handle(InvoicePaidV1 message) =>
                    throw new InvalidOperationException($"Every delivery of {message.InvoiceId} fails.");
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(Dependencies(postgres, rabbitMq));
        (await host.GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The default exchange drops a message for a queue that does not exist yet: wait for the host to declare it.
        const string Queue = "Shop.Contracts.Billing.InvoicePaidV1";
        for (var attempt = 0; attempt < 120 && !rabbitMq.RabbitMqQueues().Split('\n').Any(line => line.StartsWith(Queue + "\t", StringComparison.Ordinal)); attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        rabbitMq.PublishToRabbitMqQueue(
            Queue,
            $$"""{"type":"{{Queue}}","content_type":"application/json","message_id":"{{Guid.CreateVersion7()}}"}""",
            $$"""{"InvoiceId":"{{Guid.CreateVersion7()}}"}""");

        // Seven attempts over about a minute (the pack's ADR), then the message is dead-lettered somewhere.
        var queues = rabbitMq.RabbitMqQueues();
        for (var attempt = 0; attempt < 360 && RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword) == 0 && !HoldsMessages(queues, "wolverine-dead-letter-queue"); attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            queues = rabbitMq.RabbitMqQueues();
        }

        // The pack's ADR and agent rules name the table as the one place a dead letter is found and replayed from.
        RedeliveredEvent.WolverineDeadLetters(postgres, DatabasePassword).ShouldBe(1, $"Queues:\n{queues}");
        HoldsMessages(queues, "wolverine-dead-letter-queue").ShouldBeFalse(queues);
    }

    [Fact]
    public async Task An_event_between_modules_of_the_host_survives_the_host_stopping_before_its_consumer_succeeds()
    {
        // The event does not cross the broker (docs/adr): it is kept in Wolverine's durable local queue, stored with the
        // billing module's changes, so a host killed mid-delivery delivers it after the restart (#688).
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", PayInvoice);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", SurvivingEvent.Consumer);
        project.Write("src/Modules/Shipping/Shipping.Api/Deliveries.cs", SurvivingEvent.Endpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        var invoice = Guid.CreateVersion7();
        await SurvivingEvent.PayThenKillTheHostMidDeliveryAsync(
            project.StartHost(new Dictionary<string, string>(Dependencies(postgres, rabbitMq)) { [SurvivingEvent.Hangs] = "true" }),
            invoice);

        using var restarted = project.StartHost(Dependencies(postgres, rabbitMq));
        await SurvivingEvent.ShouldBeReceivedAfterTheRestartAsync(restarted, invoice, postgres, DatabasePassword);
    }

    /// <summary>Whether a queue in rabbitmqctl's "name count" listing holds any message.</summary>
    private static bool HoldsMessages(string queues, string queue) =>
        queues.Split('\n').Select(line => line.Split('\t')).Any(columns => columns[0] == queue && columns[^1].Trim() != "0");

    [Fact]
    public void A_tenant_cannot_read_another_tenants_rows()
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
        project.Write("tests/Modules/Billing.Tests/TenancyTests.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Shop.BuildingBlocks.Tenancy;
            using Shop.Modules.Billing.Api;
            using Shop.Modules.Billing.Domain;
            using Shop.Modules.Billing.Infrastructure.Persistence;

            namespace Shop.Modules.Billing.Tests;

            public class TenancyTests
            {
                [Fact]
                public async Task A_tenant_sees_only_its_own_rows()
                {
                    var cancellationToken = TestContext.Current.CancellationToken;
                    var services = new ServiceCollection().AddLogging().AddTenancy();
                    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                    new BillingModule().AddServices(services, configuration);
                    await using var provider = services.BuildServiceProvider();

                    using (TenantScope.Begin("tenant-a"))
                    {
                        await using var scope = provider.CreateAsyncScope();
                        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                        await context.Database.EnsureCreatedAsync(cancellationToken);
                        context.Add(new Note(Guid.CreateVersion7(), "only for tenant a"));
                        await context.SaveChangesAsync(cancellationToken);
                    }

                    (await CountAsync(provider, "tenant-b", cancellationToken)).ShouldBe(0);
                    (await CountAsync(provider, "tenant-a", cancellationToken)).ShouldBe(1);
                }

                private static async Task<int> CountAsync(ServiceProvider provider, string tenant, CancellationToken cancellationToken)
                {
                    using var tenantScope = TenantScope.Begin(tenant);
                    await using var scope = provider.CreateAsyncScope();
                    return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Set<Note>().CountAsync(cancellationToken);
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_generated_integration_tests_pass_against_the_running_aspire_stack()
    {
        using var project = new GeneratedProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 11");
    }

    [Theory]
    [InlineData("src/Modules/Billing/Billing.Domain/Invoice.cs", "## What changed\nPaid invoices.", 1)]
    [InlineData("src/Modules/Billing/Billing.Domain/Invoice.cs", "## Tests observed failing\n\n```\nfailed Billing.Tests.An_invoice_can_be_paid\n```", 0)]
    [InlineData("src/Modules/Billing/Billing.Domain/Invoice.cs", "## Tests observed passing before the change\n\n```\npassed Billing.Tests.An_invoice_can_be_paid\n```", 0)]
    [InlineData("src/Modules/Billing/Billing.Domain/Invoice.cs", "## Tests observed failing\n\n<!-- Paste the output here. -->\n\n## What changed\n```\nInvoice.Pay\n```", 1)]
    [InlineData("src/Modules/Billing/Billing.Domain/Invoice.cs", "## Tests observed passing before the change\n\n<!-- Paste the output here. -->\n\n## What changed\n```\nInvoice.Pay\n```", 1)]
    [InlineData("docs/adr/0001-base-core.md", "## What changed\nTypo.", 0)]
    [InlineData("tests/Modules/Billing.Tests/InvoiceTests.cs", "## What changed\nMore tests.", 0)]
    public void The_red_first_check_wants_failing_or_characterisation_output_when_source_changes(string changedFile, string body, int expectedExitCode)
    {
        using var project = new GeneratedProject(Profile);
        var bodyFile = Path.Combine(project.ProjectDirectory, "pr-body.md");
        var changedFiles = Path.Combine(project.ProjectDirectory, "changed-files.txt");
        File.WriteAllText(bodyFile, body);
        File.WriteAllText(changedFiles, changedFile + "\n");

        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/check-red-first.sh", bodyFile, changedFiles);

        exitCode.ShouldBe(expectedExitCode, output);
    }

    /// <summary>An aggregate in the module that raises an event, its EF configuration, and a module test that builds the module context's model.</summary>
    private static void AddAnAggregateThatRaisesEvents(GeneratedProject project, string module, string aggregate, string change, string raised)
    {
        project.WriteTypes($"src/Modules/{module}/{module}.Domain/{aggregate}s", $$"""
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.{{module}}.Domain.{{aggregate}}s;

            public sealed record {{aggregate}}Id(Guid Value);

            public sealed record {{raised}}({{aggregate}}Id {{aggregate}}) : IDomainEvent;

            public sealed class {{aggregate}}({{aggregate}}Id id) : AggregateRoot<{{aggregate}}Id>(id)
            {
                public bool Done { get; private set; }

                public void {{change}}()
                {
                    Done = true;
                    Raise(new {{raised}}(Id));
                }
            }
            """);
        project.Write($"src/Modules/{module}/{module}.Infrastructure/Persistence/Configurations/{aggregate}Configuration.cs", $$"""
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.{{module}}.Domain.{{aggregate}}s;

            namespace Shop.Modules.{{module}}.Infrastructure.Persistence.Configurations;

            public sealed class {{aggregate}}Configuration : IEntityTypeConfiguration<{{aggregate}}>
            {
                public void Configure(EntityTypeBuilder<{{aggregate}}> builder)
                {
                    builder.ToTable("{{aggregate}}s");
                    builder.HasKey(row => row.Id);
                    builder.Property(row => row.Id).HasConversion(id => id.Value, value => new {{aggregate}}Id(value));
                    builder.Property(row => row.Done);
                }
            }
            """);
        project.Write($"tests/Modules/{module}.Tests/ModelTests.cs", $$"""
            using Shop.Modules.{{module}}.Domain.{{aggregate}}s;
            using Shop.Modules.{{module}}.Infrastructure.Persistence;

            namespace Shop.Modules.{{module}}.Tests;

            public class ModelTests
            {
                [Fact]
                public void The_context_maps_an_aggregate_that_raises_domain_events_without_its_events()
                {
                    using var context = new {{module}}DbContextFactory().CreateDbContext([]);

                    var mapped = context.Model.FindEntityType(typeof({{aggregate}})).ShouldNotBeNull();

                    mapped.FindProperty(nameof({{aggregate}}.DomainEvents)).ShouldBeNull();
                    mapped.FindNavigation(nameof({{aggregate}}.DomainEvents)).ShouldBeNull();
                }
            }
            """);
    }
}

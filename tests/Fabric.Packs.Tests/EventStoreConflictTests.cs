using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Fabric.Packs.Tests;

/// <summary>
/// module.event-store at the HTTP edge (#645): two creates of one aggregate id, the loser's append to a stream that
/// should not exist yet is a conflict and answers 409 <c>already-exists</c>, as a unique index's refusal does with EF
/// Core (UniqueConstraintTests). A lost update on a stream that already exists is not a duplicate and stays a 500.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class EventStoreConflictTests
{
    private const string Profile = "lean-crud";
    private const string DatabasePassword = "fabric-tests";

    /// <summary>The image the KurrentDB pack's AppHost runs (KurrentDbTests).</summary>
    private const string KurrentDbImage = "docker.kurrent.io/kurrent-latest/kurrentdb:26.1.1";

    private const string Invoice = """
        using Shop.BuildingBlocks.EventSourcing;

        namespace Shop.Modules.Billing.Domain.Invoices;

        public sealed record InvoiceId(Guid Value) : IAggregateId;

        public sealed record InvoiceIssued(Guid Invoice, decimal Amount) : IDomainEvent;

        public sealed record InvoicePaid(Guid Invoice) : IDomainEvent;

        public sealed class Invoice : EventSourcedAggregate<InvoiceId>
        {
            private Invoice()
            {
            }

            public decimal Amount { get; private set; }

            public bool IsPaid { get; private set; }

            public static Invoice Issue(InvoiceId id, decimal amount)
            {
                Ensure.That(id).IsNotNull();

                var invoice = new Invoice();
                invoice.Raise(new InvoiceIssued(id.Value, amount));
                return invoice;
            }

            public void Pay() => Raise(new InvoicePaid(Id.Value));

            private void Apply(InvoiceIssued issued)
            {
                Id = new InvoiceId(issued.Invoice);
                Amount = issued.Amount;
            }

            private void Apply(InvoicePaid paid) => IsPaid = paid.Invoice == Id.Value;
        }
        """;

    /// <summary>
    /// Issues without looking for the invoice first, as the loser of a race past an application-level check would; and
    /// pays from two copies loaded together, so the second save loses to the first on a stream that exists.
    /// </summary>
    private const string InvoiceEndpoints = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Api;

        public sealed class InvoiceEndpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPost("/invoices/{id}", async (Guid id, IBillingAggregateStore invoices, CancellationToken cancellationToken) =>
                {
                    await invoices.SaveAsync(Invoice.Issue(new InvoiceId(id), 42m), cancellationToken);
                    return Results.NoContent();
                }).AllowAnonymous();
                endpoints.MapPost("/invoices/{id}/pay-twice", async (Guid id, IBillingAggregateStore invoices, CancellationToken cancellationToken) =>
                {
                    var first = (await invoices.LoadAsync<Invoice>(new InvoiceId(id), cancellationToken)).Value;
                    var second = (await invoices.LoadAsync<Invoice>(new InvoiceId(id), cancellationToken)).Value;
                    first.Pay();
                    await invoices.SaveAsync(first, cancellationToken);
                    second.Pay();
                    await invoices.SaveAsync(second, cancellationToken);
                    return Results.NoContent();
                }).AllowAnonymous();
            }
        }
        """;

    [Theory]
    [InlineData("marten")]
    [InlineData("kurrentdb")]
    public async Task Two_creates_of_one_aggregate_answer_409_problem_details_to_the_loser_and_a_lost_update_stays_a_500(string store)
    {
        using var project = new GeneratedProject(Profile, "module.style=event-sourced", $"module.event-store={store}", "module.cqrs=logical");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", Invoice);
        project.Write("src/Modules/Billing/Billing.Api/InvoiceEndpoints.cs", InvoiceEndpoints);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var kurrentDb = store == "kurrentdb" ? Container.KurrentDb(KurrentDbImage) : null;
        var environment = new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        };
        if (kurrentDb is not null)
        {
            environment["ConnectionStrings__eventstore"] = $"kurrentdb://127.0.0.1:{kurrentDb.Port.ToString(CultureInfo.InvariantCulture)}?tls=false";
        }

        var (migrateExitCode, migrateOutput) = project.DotNetCommand(environment, $"src/MigrationRunner/bin/Release/{GeneratedProject.TargetFramework}/MigrationRunner.dll");
        migrateExitCode.ShouldBe(0, migrateOutput);

        using var host = project.StartHost(new Dictionary<string, string>(environment)
        {
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        var id = Guid.CreateVersion7();
        var race = await Task.WhenAll(host.PostAsync($"/billing/invoices/{id}"), host.PostAsync($"/billing/invoices/{id}"));
        var winner = race.Single(response => response.StatusCode == HttpStatusCode.NoContent);
        var loser = race.Single(response => response != winner);
        await ShouldBeAlreadyExistsAsync(loser, id);

        // A later create of the same id, the race long decided, is refused the same way.
        using var late = await host.PostAsync($"/billing/invoices/{id}");
        await ShouldBeAlreadyExistsAsync(late, id);

        using var lostUpdate = await host.PostAsync($"/billing/invoices/{id}/pay-twice");
        var failure = await lostUpdate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        lostUpdate.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, failure);
        failure.ShouldNotContain("already-exists");

        foreach (var response in race)
        {
            response.Dispose();
        }
    }

    private static async Task ShouldBeAlreadyExistsAsync(HttpResponseMessage response, Guid id)
    {
        var conflict = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, conflict);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", conflict);
        using (var problem = JsonDocument.Parse(conflict))
        {
            problem.RootElement.GetProperty("status").GetInt32().ShouldBe(409, conflict);
            problem.RootElement.GetProperty("code").GetString().ShouldBe("already-exists", conflict);
        }

        // Nothing about the stream reaches the caller.
        conflict.ShouldNotContain(id.ToString());
    }
}

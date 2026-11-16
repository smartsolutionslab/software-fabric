namespace Fabric.Packs.Tests;

/// <summary>
/// A consumer in the shipping module that hangs on InvoicePaidV1 while the host runs with <see cref="Hangs"/> set, and
/// otherwise records it, with an endpoint that reads both back. Shared by the bus packs' tests that stop a host between
/// the commit of an event and its consumer's success, then start it again (#688).
/// </summary>
internal static class SurvivingEvent
{
    /// <summary>The environment variable that makes the consumer hang, so the host can be stopped mid-delivery.</summary>
    public const string Hangs = "SHIPPING_CONSUMER_HANGS";

    public const string Consumer = """
        using System.Collections.Concurrent;
        using Shop.Contracts.Billing;

        namespace Shop.Modules.Shipping.Application;

        public sealed class InvoicePaidConsumer
        {
            public static ConcurrentBag<Guid> Started { get; } = [];

            public static ConcurrentBag<Guid> Received { get; } = [];

            public static async Task HandleAsync(InvoicePaidV1 message, CancellationToken cancellationToken)
            {
                if (Environment.GetEnvironmentVariable("SHIPPING_CONSUMER_HANGS") == "true")
                {
                    Started.Add(message.InvoiceId);
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                Received.Add(message.InvoiceId);
            }
        }
        """;

    public const string Endpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.Modules.Shipping.Application;

        namespace Shop.Modules.Shipping.Api;

        public sealed class Deliveries : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGet("/started", () => InvoicePaidConsumer.Started.ToArray()).AllowAnonymous();
                endpoints.MapGet("/received", () => InvoicePaidConsumer.Received.ToArray()).AllowAnonymous();
            }
        }
        """;

    /// <summary>
    /// Pays an invoice on a host whose consumer hangs, waits until the consumer has the event, then kills the host:
    /// the publishing module's transaction is committed, its consumer has not succeeded.
    /// </summary>
    public static async Task PayThenKillTheHostMidDeliveryAsync(RunningHost host, Guid invoice)
    {
        using (host)
        {
            (await host.PostAsync($"/billing/invoices/{invoice}/pay")).StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);

            var started = "[]";
            for (var attempt = 0; attempt < 120 && !started.Contains(invoice.ToString(), StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
                started = await (await host.GetAsync("/shipping/started")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            }

            started.ShouldContain(invoice.ToString(), Case.Sensitive, "The consumer never received the event before the host was stopped.");
        }
    }

    /// <summary>Waits for the restarted host's consumer to receive the event, or fails with what the database holds.</summary>
    public static async Task ShouldBeReceivedAfterTheRestartAsync(RunningHost host, Guid invoice, Container postgres, string password)
    {
        var received = "[]";
        for (var attempt = 0; attempt < 480 && !received.Contains(invoice.ToString(), StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            received = await (await host.GetAsync("/shipping/received")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        received.ShouldContain(
            invoice.ToString(),
            Case.Sensitive,
            $"Received after the restart: {received}; Wolverine's incoming envelopes: {IncomingEnvelopes(postgres, password)}.");
    }

    /// <summary>The rows of Wolverine's durable inbox; 0 while its schema does not exist yet.</summary>
    private static string IncomingEnvelopes(Container postgres, string password) =>
        postgres.Psql(password, "select to_regclass('wolverine.wolverine_incoming_envelopes') is not null") == "t"
            ? postgres.Psql(password, "select count(*) from wolverine.wolverine_incoming_envelopes")
            : "0 (no table)";
}

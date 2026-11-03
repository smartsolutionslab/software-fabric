using System.Text.Json;

namespace Fabric.Packs.Tests;

/// <summary>
/// A consumer in the shipping module that fails the first delivery of InvoicePaidV1 and records the metadata of every
/// delivery, and an endpoint that reads the record back. Shared by the bus packs' cross-module tests: each transport must
/// deliver an identifier and a time, the same on a redelivery.
/// </summary>
internal static class RedeliveredEvent
{
    /// <param name="tenancy">Whether the project has tenancy, so the metadata carries a TenantId.</param>
    public static string Consumer(bool tenancy) => ConsumerSource.Replace(
        "$tenant$",
        tenancy ? "$\"TenantId = {message.Metadata.TenantId}\"" : string.Empty,
        StringComparison.Ordinal);

    private const string ConsumerSource = """
        using System.Collections.Concurrent;
        using System.Globalization;
        using Shop.Contracts.Billing;

        namespace Shop.Modules.Shipping.Application;

        public sealed class InvoicePaidConsumer
        {
            private readonly ConcurrentQueue<string[]> deliveries = Deliveries;

            public static ConcurrentBag<Guid> Received { get; } = [];

            /// <summary>The metadata of every delivery: identifier, occurred-at and, with tenancy, the tenant.</summary>
            public static ConcurrentQueue<string[]> Deliveries { get; } = [];

            public Task Handle(InvoicePaidV1 message, CancellationToken cancellationToken)
            {
                deliveries.Enqueue(Describe(message));
                if (deliveries.Count == 1)
                {
                    throw new InvalidOperationException("The first delivery fails, so the event arrives a second time.");
                }

                Received.Add(message.InvoiceId);
                return Task.CompletedTask;
            }

            private static string[] Describe(InvoicePaidV1 message) =>
            [
                $"EventIdentifier = {message.Metadata.EventIdentifier:D}",
                $"OccurredAt = {message.Metadata.OccurredAt.ToString("O", CultureInfo.InvariantCulture)}",
                $tenant$
            ];
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
                endpoints.MapGet("/received", () => InvoicePaidConsumer.Received.Count).AllowAnonymous();
                endpoints.MapGet("/deliveries", () => InvoicePaidConsumer.Deliveries.Select(delivery => string.Join(", ", delivery)).ToArray()).AllowAnonymous();
            }
        }
        """;

    /// <summary>
    /// Wolverine retries no failed message by itself: it moves it to its dead letters in the database. Marking it
    /// replayable is how it is delivered again, the same envelope a second time; this waits until it is there to mark.
    /// </summary>
    public static async Task ReplayWolverineDeadLettersAsync(Container postgres, string password)
    {
        const string Replay = """
            with replayed as (update wolverine.wolverine_dead_letters set replayable = true where replayable = false returning 1)
            select count(*) from replayed
            """;

        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (postgres.Psql(password, "select to_regclass('wolverine.wolverine_dead_letters') is not null") == "t" && postgres.Psql(password, Replay) != "0")
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        throw new ShouldAssertException("The failed delivery never reached Wolverine's dead letters.");
    }

    /// <summary>
    /// Waits for the second delivery, then checks that both carried the same metadata: a version 7 identifier, a time
    /// between <paramref name="publishedAfter"/> and now, and the tenant when one is expected.
    /// </summary>
    /// <param name="redeliver">What delivers the failed event again, once it failed; null when the transport retries by itself.</param>
    public static async Task ShouldHaveBeenDeliveredTwiceWithTheSameMetadataAsync(RunningHost host, DateTimeOffset publishedAfter, string? tenant, int attempts, Func<Task>? redeliver = null)
    {
        if (redeliver is not null)
        {
            await redeliver();
        }

        var received = "0";
        for (var attempt = 0; attempt < attempts && received == "0"; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            received = await (await host.GetAsync("/shipping/received")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        var body = await (await host.GetAsync("/shipping/deliveries")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        received.ShouldBe("1", $"Deliveries: {body}");

        var deliveries = JsonSerializer.Deserialize<string[]>(body).ShouldNotBeNull();
        deliveries.Length.ShouldBe(2, body);
        deliveries.Distinct().ShouldHaveSingleItem($"A redelivery carries the metadata of the first delivery: {body}");

        var metadata = deliveries[0];
        var identifier = Guid.Parse(Field(metadata, "EventIdentifier"));
        identifier.ShouldNotBe(Guid.Empty, metadata);
        identifier.Version.ShouldBe(7, metadata);

        var occurredAt = DateTimeOffset.Parse(Field(metadata, "OccurredAt"), System.Globalization.CultureInfo.InvariantCulture);
        occurredAt.ShouldBeInRange(publishedAfter.AddSeconds(-1), TimeProvider.System.GetUtcNow(), metadata);

        if (tenant is not null)
        {
            Field(metadata, "TenantId").ShouldBe(tenant, metadata);
        }
    }

    /// <summary>A member's value from the printed metadata: "EventIdentifier = ..., OccurredAt = ...".</summary>
    private static string Field(string printed, string name)
    {
        var start = printed.IndexOf($"{name} = ", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"{printed} has no {name}.");
        start += name.Length + 3;
        var end = printed.IndexOf(',', start);
        return (end < 0 ? printed[start..] : printed[start..end]).Trim();
    }
}

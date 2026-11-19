using System.Net;

namespace Shop.Integration.Tests;

/// <summary>
/// Two services exchange one integration event through the running application: the billing service publishes
/// InvoicePaidV1 through its outbox, and RabbitMQ carries it to the shipping service's own queue, whose consumer dispatches
/// a command that records it in the shipping service's database, as a sample named after the invoice.
/// </summary>
public class ServicesExchangeAnEventTests(AspireFixture aspire)
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task An_event_one_service_publishes_through_its_outbox_reaches_the_service_that_consumes_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");
        var invoice = Guid.CreateVersion7();

        using var paid = await client.PostAsync(new Uri($"/billing/invoices/{invoice}/pay", UriKind.Relative), null, cancellationToken);

        paid.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var recorded = $"Invoice {invoice} paid";
        var received = false;
        for (var waited = TimeSpan.Zero; !received && waited < Patience; waited += TimeSpan.FromSeconds(1))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var samples = await client.GetStringAsync(new Uri("/shipping/samples?pageSize=100", UriKind.Relative), cancellationToken);
            received = samples.Contains(recorded, StringComparison.Ordinal);
        }

        received.ShouldBeTrue($"the shipping service did not receive invoice {invoice} within {Patience}.");
    }
}

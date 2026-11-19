using System.Net;
using System.Net.Http.Json;

namespace Shop.Integration.Tests;

/// <summary>
/// Two services exchange one integration event through the running application: the billing service publishes
/// InvoicePaidV1 through its outbox, RabbitMQ carries it to the shipping service's own queue, and the shipping service's
/// consumer records it as a sample in its own database.
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
        var name = $"Invoice {invoice} paid";

        using var paid = await client.PostAsync(new Uri($"/billing/invoices/{invoice}/pay", UriKind.Relative), null, cancellationToken);

        paid.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var received = false;
        for (var waited = TimeSpan.Zero; !received && waited < Patience; waited += TimeSpan.FromSeconds(1))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var page = await client.GetFromJsonAsync<SamplePage>(new Uri("/shipping/samples?page=1&pageSize=100", UriKind.Relative), cancellationToken);
            received = page!.Items.Any(sample => sample.Name == name);
        }

        received.ShouldBeTrue($"the shipping service did not record '{name}' within {Patience}.");
    }

    private sealed record Sample(Guid Id, string Name);

    private sealed record SamplePage(IReadOnlyList<Sample> Items, int TotalCount, int Page, int PageSize);
}

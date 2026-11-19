using Shop.Contracts.Billing;
using Shop.Modules.Shipping.Application;

namespace Shop.Modules.Shipping.Tests;

public class InvoicePaidConsumerTests
{
    [Fact]
    public void A_paid_invoice_is_recorded_as_received()
    {
        var invoice = Guid.CreateVersion7();

        InvoicePaidConsumer.Handle(new InvoicePaidV1(invoice));

        InvoicePaidConsumer.Received.ShouldContain(invoice);
    }
}

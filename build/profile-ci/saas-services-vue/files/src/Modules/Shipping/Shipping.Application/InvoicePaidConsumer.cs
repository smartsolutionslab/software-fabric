using System.Collections.Concurrent;
using Shop.Contracts.Billing;

namespace Shop.Modules.Shipping.Application;

/// <summary>
/// Consumes the billing service's <see cref="InvoicePaidV1"/> on the shipping service's own queue and keeps the invoices
/// it received, which the services' end-to-end test reads back.
/// </summary>
public static class InvoicePaidConsumer
{
    public static ConcurrentQueue<Guid> Received { get; } = new();

    public static void Handle(InvoicePaidV1 message)
    {
        Ensure.That(message).IsNotNull();

        Received.Enqueue(message.InvoiceId);
    }
}

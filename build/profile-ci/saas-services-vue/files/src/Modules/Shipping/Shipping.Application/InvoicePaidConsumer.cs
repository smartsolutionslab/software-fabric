using Shop.BuildingBlocks.Messaging;
using Shop.Contracts.Billing;
using Shop.Modules.Shipping.Application.Commands.CreateSample;
using Shop.Modules.Shipping.Domain.Samples;

namespace Shop.Modules.Shipping.Application;

/// <summary>
/// Consumes the billing service's <see cref="InvoicePaidV1"/> on the shipping service's own queue and dispatches a command
/// that records it in the shipping service's own database, as a sample named after the invoice, which the services'
/// end-to-end test reads back.
/// </summary>
public sealed class InvoicePaidConsumer(IDispatcher dispatcher)
{
    public async Task HandleAsync(InvoicePaidV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var created = await dispatcher.SendAsync(new CreateSample(SampleName.From($"Invoice {message.InvoiceId} paid")), cancellationToken);
        if (created.IsFailure)
        {
            // Thrown, so Wolverine retries the event and dead-letters it in the end, rather than losing it.
            throw new InvalidOperationException($"Invoice {message.InvoiceId} was not recorded: {created.Error.Code}.");
        }
    }
}

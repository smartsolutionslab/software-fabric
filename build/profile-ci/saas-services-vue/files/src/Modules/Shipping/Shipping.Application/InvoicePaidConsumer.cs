using Shop.BuildingBlocks.Messaging;
using Shop.Contracts.Billing;
using Shop.Modules.Shipping.Application.Commands.CreateSample;
using Shop.Modules.Shipping.Domain.Samples;

namespace Shop.Modules.Shipping.Application;

/// <summary>
/// Consumes the billing service's <see cref="InvoicePaidV1"/> on the shipping service's own queue and records it as a
/// sample in the shipping service's database. A redelivered message is dropped by the durable inbox, by its envelope id.
/// </summary>
public sealed class InvoicePaidConsumer(IDispatcher dispatcher)
{
    public async Task HandleAsync(InvoicePaidV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var recorded = await dispatcher.SendAsync(new CreateSample(SampleName.From($"Invoice {message.InvoiceId} paid")), cancellationToken);
        if (recorded.IsFailure)
        {
            throw new InvalidOperationException($"Invoice {message.InvoiceId} was not recorded: {recorded.Error.Message}");
        }
    }
}

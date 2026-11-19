using Shop.BuildingBlocks.Messaging;
using Shop.Contracts.Billing;
using Shop.Modules.Shipping.Application;
using Shop.Modules.Shipping.Application.Commands.CreateSample;

namespace Shop.Modules.Shipping.Tests;

public class InvoicePaidConsumerTests
{
    [Fact]
    public async Task A_paid_invoice_is_recorded_as_a_sample_named_after_it()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<CreateSample>(), Arg.Any<CancellationToken>()).Returns(Result<Guid, ApiError>.Success(Guid.CreateVersion7()));
        var invoice = Guid.CreateVersion7();

        await new InvoicePaidConsumer(dispatcher).HandleAsync(new InvoicePaidV1(invoice), TestContext.Current.CancellationToken);

        await dispatcher.Received(1).SendAsync(Arg.Is<CreateSample>(command => command.Name.Value == $"Invoice {invoice} paid"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_paid_invoice_that_cannot_be_recorded_throws_so_the_message_is_tried_again()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<CreateSample>(), Arg.Any<CancellationToken>()).Returns(Result<Guid, ApiError>.Failure(SampleFailures.InvalidName()));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new InvoicePaidConsumer(dispatcher).HandleAsync(new InvoicePaidV1(Guid.CreateVersion7()), TestContext.Current.CancellationToken));
    }
}

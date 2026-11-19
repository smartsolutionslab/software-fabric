using Shop.BuildingBlocks.Api;
using Shop.Contracts.Billing;
using Shop.Modules.Billing.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Shop.Modules.Billing.Api.Endpoints;

/// <summary>
/// Publishes <see cref="InvoicePaidV1"/> through the billing service's outbox, so the services' end-to-end test can see
/// it reach the shipping service. Not part of the API the frontend's client is generated from.
/// </summary>
public sealed class PayInvoiceEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/invoices/{id:guid}/pay", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            await unitOfWork.PublishAsync(new InvoicePaidV1(id));
            await unitOfWork.CommitAsync(cancellationToken);
            return Results.Accepted();
        }).ExcludeFromDescription();
    }
}

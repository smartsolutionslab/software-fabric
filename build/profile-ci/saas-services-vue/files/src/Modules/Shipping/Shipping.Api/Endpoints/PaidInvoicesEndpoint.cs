using Shop.BuildingBlocks.Api;
using Shop.Modules.Shipping.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Shop.Modules.Shipping.Api.Endpoints;

/// <summary>
/// The invoices the shipping service received as <c>InvoicePaidV1</c>, for the services' end-to-end test. Not part of the
/// API the frontend's client is generated from.
/// </summary>
public sealed class PaidInvoicesEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/invoices/paid", () => TypedResults.Ok(InvoicePaidConsumer.Received.ToArray())).ExcludeFromDescription();
    }
}

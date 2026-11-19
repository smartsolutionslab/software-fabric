namespace Shop.Contracts.Billing;

/// <summary>An invoice was paid: the billing service publishes it, the shipping service consumes it.</summary>
public sealed record InvoicePaidV1(Guid InvoiceId) : IntegrationEvent;

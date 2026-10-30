## Module style: domain-driven design

- Business rules live in the Domain project: one folder per aggregate holding the aggregate root, its entities, value objects and `Events/`.
- An aggregate root derives from `AggregateRoot<TId>` and changes state only through methods named after what happens (`invoice.Pay(amount)`), which check invariants and raise domain events.
- Identities are strongly typed records (`InvoiceId`), not raw `Guid`s.
- The Domain project references nothing but the BCL and `BuildingBlocks.Domain`: no EF Core attributes, no ASP.NET, no Contracts.

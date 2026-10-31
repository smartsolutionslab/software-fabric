## Module style: domain-driven design

- Business rules live in the Domain project: one folder per aggregate holding the aggregate root, its entities, value objects and `Events/`.
- An aggregate root derives from `AggregateRoot<TId>` and changes state only through methods named after what happens (`invoice.Pay()`), which check invariants and raise domain events.
{{ if project["backend.errors"] == "result" }}- A broken invariant is a value, not an exception: the method returns `Result<T, DomainError>` (`Result<Unit, DomainError>` when nothing comes back), and its errors are sealed records deriving from `DomainError` beside the aggregate. They carry a code and a message, never an HTTP status: the Application maps them to `ApiError`. An architecture rule fails if a Domain type is or returns an `ApiError`.
{{ end }}- Identities are strongly typed records (`InvoiceId`), not raw `Guid`s.
- The Domain project references nothing but the BCL, `Kernel` and `BuildingBlocks.Domain`: no EF Core attributes, no ASP.NET, no Contracts.

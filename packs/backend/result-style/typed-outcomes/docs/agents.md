## Result style: typed outcomes

- Return `Success(value)` or `Failure(error)` — never name `Result<,>`'s type arguments again; the return type does that.
- A module's errors are sealed records, grouped in a static class whose factories return the base type (generics are invariant):
  - Application: `<Name>Failures`, returning `ApiError`: `public static ApiError NotFound(InvoiceId id) => new InvoiceNotFound(id);`. It also maps the domain's errors: `public static ApiError From(DomainError error) => error switch { InvoiceAlreadyPaid => ApiError.From(error, HttpStatusCode.Conflict), ... };`.
  - Domain: `<Name>Errors`, returning `DomainError`: `public static DomainError AlreadyPaid(InvoiceId id) => new InvoiceAlreadyPaid(id);`.
- Use `if` with two returns instead of `?:` between `Success(...)` and `Failure(...)`.

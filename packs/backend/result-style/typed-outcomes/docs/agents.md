## Result style: typed outcomes

- Return `Success(value)` or `Failure(error)` — never name `Result<,>`'s type arguments again; the return type does that.
- A module's errors are sealed records, grouped in a static class whose factories return the base type (generics are invariant):
  - Application: `<Name>Failures`, returning `ApiError`: `public static ApiError NotFound(InvoiceId id) => new InvoiceNotFound(id);`. It also maps the domain's errors: `public static ApiError From(DomainError error) => error switch { InvoiceAlreadyPaid => ApiError.From(error, HttpStatusCode.Conflict), ... };`.
  - Domain: `<Name>Errors`, returning `DomainError`: `public static DomainError AlreadyPaid(InvoiceId id) => new InvoiceAlreadyPaid(id);`.
- Use `if` with two returns instead of `?:` between `Success(...)` and `Failure(...)`.
- A result is a union of `SuccessOutcome<TValue>` and `FailureOutcome<TError>`. To branch on it, `switch` on the cases: `result switch { SuccessOutcome<InvoiceDto> success => ..., FailureOutcome<ApiError> failure => ... }`; leaving one out does not compile. Never add a discard arm (`_ =>`) to such a switch: it hides the case the compiler would name.
- When a domain operation fails in a fixed set of ways, declare them as a union of their records beside them, `public union InvoiceError(InvoiceAlreadyPaid, InvoiceOverdue);`, return it from the `<Name>Errors` factories and `Result<..., InvoiceError>`, and map it in `<Name>Failures.From(InvoiceError error)` with a `switch` that has no discard arm. A new error then fails the build until it is mapped.

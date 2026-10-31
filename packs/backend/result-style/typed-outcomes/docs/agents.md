## Result style: typed outcomes

- Return `Success(value)` or `Failure(error)` — never name `Result<,>`'s type arguments again; the return type does that.
- A module's errors are sealed records deriving from `ApiError`, grouped in a `<Name>Failures` static class whose factories return `ApiError`: `public static ApiError NotFound(InvoiceId id) => new InvoiceNotFound(id);`.
- Use `if` with two returns instead of `?:` between `Success(...)` and `Failure(...)`.

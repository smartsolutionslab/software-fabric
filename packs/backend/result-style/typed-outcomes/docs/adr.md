# Result style: typed outcomes

**Status:** Accepted (generated)

## Decision

`Result<TValue, TError>` is a `readonly struct` (no allocation, never null) with `IsSuccess`, `IsFailure`, `Value`, `Error` and `Match`. A handler names only what it carries: `return Success(dto);` or `return Failure(InvoiceFailures.NotFound(id));` — the static `Outcome.Success` / `Outcome.Failure` are imported everywhere with `using static`, and the return type supplies the other type argument through an implicit conversion. They are on `Outcome`, not on a static `Result`: a generated project has one type per file, and `Result.cs` holds the struct.

Errors are sealed records deriving from the abstract `ApiError(Code, Message, Status)`. Generics are invariant, so a failure must be built as the error *base* the result declares: each module groups its errors behind a `<Name>Failures` class whose factories return `ApiError`.

This is the style of Smart Sentinel Eye.

## Consequences

- No `Result<InvoiceDto, ApiError>.Success(...)` noise in handlers.
- A conditional expression cannot mix `Success(...)` and `Failure(...)`; use `if` and two returns.

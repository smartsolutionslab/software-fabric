# Result style: typed outcomes

**Status:** Accepted (generated)

## Decision

`Result<TValue, TError>` is a `readonly struct` (no allocation, never null) with `IsSuccess`, `IsFailure`, `Value`, `Error` and `Match`. It is a C# 15 union of its two cases, `SuccessOutcome<TValue>` and `FailureOutcome<TError>`: a `switch` on a result matches them (`SuccessOutcome<InvoiceDto> success => ...`, `FailureOutcome<ApiError> failure => ...`), and one that misses a case does not compile. The union's members live on its nested `IUnionMembers` interface, so `Value` stays the success's value, and a case is matched without boxing. A handler names only what it carries: `return Success(dto);` or `return Failure(InvoiceFailures.NotFound(id));` — the static `Outcome.Success` / `Outcome.Failure` are imported everywhere with `using static`, and the return type supplies the other type argument through the union's conversion from each case. They are on `Outcome`, not on a static `Result`: a generated project has one type per file, and `Result.cs` holds the struct.

Errors are sealed records deriving from the abstract `ApiError(Code, Message, Status)`. Generics are invariant, so a failure must be built as the error *base* the result declares: each module groups its errors behind a `<Name>Failures` class whose factories return `ApiError`.

This is the style of Smart Sentinel Eye.

## Consequences

- No `Result<InvoiceDto, ApiError>.Success(...)` noise in handlers.
- An unhandled case no longer compiles: a `switch` on a result that forgets the failure is a build error (CS8509), where an `IsSuccess` check lets a branch be left out by a missing `else`.
- A module whose domain operation fails in a fixed set of ways can say so with a union of those errors (`public union InvoiceError(InvoiceAlreadyPaid, InvoiceOverdue);`), and the Application maps them with an exhaustive `switch`: a new domain error then fails the build where it is not yet mapped, instead of falling to a default arm.
- A conditional expression cannot mix `Success(...)` and `Failure(...)`; use `if` and two returns.

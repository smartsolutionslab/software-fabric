# Result style: minimal

**Status:** Accepted (generated)

## Decision

`Result<TValue, TError>` is a small sealed class with `Success`, `Failure`, `IsSuccess`, `Value`, `Error` and `Match`; call sites name both type arguments (`Result<InvoiceDto, ApiError>.Success(dto)`). `ApiError(Code, Message, Status)` is a record with factories for the common cases (`ApiError.NotFound(...)`).

The result is a C# 15 union of its two cases, `SuccessOutcome<TValue>` and `FailureOutcome<TError>`: a `switch` on a result matches them, and one that misses a case does not compile. The union's members live on its nested `IUnionMembers` interface, so `Value` stays the success's value, and a case is matched without boxing.

## Consequences

- Fewer moving parts than typed outcomes, at the cost of repeating the type arguments in handlers.
- An unhandled case no longer compiles: a `switch` on a result that forgets the failure is a build error (CS8509).

# Result style: minimal

**Status:** Accepted (generated)

## Decision

`Result<TValue, TError>` is a small sealed class with `Success`, `Failure`, `IsSuccess`, `Value`, `Error` and `Match`; call sites name both type arguments (`Result<InvoiceDto, ApiError>.Success(dto)`). `ApiError(Code, Message, Status)` is a record with factories for the common cases (`ApiError.NotFound(...)`).

## Consequences

- Fewer moving parts than typed outcomes, at the cost of repeating the type arguments in handlers.

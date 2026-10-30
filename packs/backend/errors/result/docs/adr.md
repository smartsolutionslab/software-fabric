# Errors: Result values

**Status:** Accepted (generated)

## Decision

Expected failures — not found, validation, conflict, forbidden — are values: handlers return `Result<TValue, TError>` where `TError` derives from `ApiError(Code, Message, Status)`, both in `src/Kernel` (their exact shape is the result style's ADR). Exceptions are for the unexpected only.

At the HTTP edge, `ToHttpResult()` turns a success into `200` with the value and a failure into RFC 9457 problem details with the error's status and code.

## Consequences

- A caller cannot ignore a failure without saying so; the compiler shows every place a failure is handled.
- No exception filter decides status codes; the error that knows the failure carries its status.

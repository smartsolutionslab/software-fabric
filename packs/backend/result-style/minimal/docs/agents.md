## Result style: minimal

- Return `Result<TValue, ApiError>.Success(value)` or `.Failure(ApiError.NotFound("billing.invoice-not-found", "..."))`.
- Errors are `ApiError` values or records deriving from it; name such types `...Error`.
- A domain rule fails with a record deriving from `DomainError` (no HTTP); Application turns it into an `ApiError` with `ApiError.From(error, status)`.
- To branch on a result, `switch` on its cases: `result switch { SuccessOutcome<InvoiceDto> success => ..., FailureOutcome<ApiError> failure => ... }`; leaving one out does not compile. Never add a discard arm (`_ =>`) to such a switch.

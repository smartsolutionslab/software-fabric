## Result style: minimal

- Return `Result<TValue, ApiError>.Success(value)` or `.Failure(ApiError.NotFound("billing.invoice-not-found", "..."))`.
- Errors are `ApiError` values or records deriving from it; name such types `...Error`.

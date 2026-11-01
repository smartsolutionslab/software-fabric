## Errors: Result values

- Handlers return `Result<TValue, TError>` (`src/Kernel`). Return `Result.Failure(...)` for anything a caller should expect; throw only for bugs and broken infrastructure.
- `ApiError` (code, message, `HttpStatusCode`) is what callers see, and it belongs to the module's Application project: each error has a stable `Code` (`billing.invoice-not-found`) and the right status.
- A rule the domain enforces fails with a `DomainError` (code and message, no HTTP) from the Domain project; the Application maps it with `ApiError.From(error, status)`, choosing the status there. `Result.MapError(...)` does it in one call.
- An operation that succeeds with nothing to return yields `Unit.Value`.
- Endpoints end with `.ToHttpResult()`; do not build problem details by hand.
- A guard or value-object factory that rejects a caller's input throws `ArgumentException` (or a subclass); the host answers it with `400` problem details, code `invalid-argument`. That is a backstop for preconditions, not a way to report errors: a failure a caller should expect is a `Result`. Throw `ArgumentException` only for bad input, never for a bug; every other exception answers `500` without details.

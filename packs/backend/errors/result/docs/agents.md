## Errors: Result values

- Handlers return `Result<TValue, TError>` (`src/BuildingBlocks/Results`). Return `Result.Failure(...)` for anything a caller should expect; throw only for bugs and broken infrastructure.
- Define a module's errors as static members of an `ApiError`-derived record in the module's Application project, each with a stable `Code` (`billing.invoice-not-found`) and the right `HttpStatusCode`.
- Endpoints end with `.ToHttpResult()`; do not build problem details by hand.

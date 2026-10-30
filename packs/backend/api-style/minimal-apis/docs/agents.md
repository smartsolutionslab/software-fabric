## API style: minimal APIs

- An endpoint is a class implementing `IEndpoint` in the module's Api project, under `Endpoints/`. It maps routes relative to the module's group: `endpoints.MapGet("/invoices/{id}", ...)` becomes `/billing/invoices/{id}`.
- Endpoints translate HTTP to a command or query, dispatch it, and return `.ToHttpResult()`. No business logic in endpoints.
- No MVC controllers; an architecture rule fails the build if one appears.

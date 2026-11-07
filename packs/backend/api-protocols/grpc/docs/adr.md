{{~ auth = project["backend.auth"] != "none" ~}}
# API protocol: gRPC beside REST

**Status:** Accepted (generated)

## Decision

Modules may serve gRPC beside their REST endpoints, through ASP.NET Core's gRPC server (`Grpc.AspNetCore`). REST stays: the frontend's client is generated from the OpenAPI document, which gRPC services are not part of.

- **The contract is a `.proto` file, contract first**, in the module's Api project under `Protos/`. `Grpc.Tools` compiles every `Protos/**/*.proto` of a module's Api project at build time into a server base and a client (`GrpcServices="Both"`); nothing generated is checked in, so a proto and its C# cannot drift. A proto names its C# namespace `<Namespace>.Modules.<Module>.Api.GrpcServices` and its package after the module (`package billing;`), so two modules' services never share a name.
- **A gRPC service is an adapter, as an endpoint is.** It implements the generated base in the module's Api project, under `GrpcServices/`, translates each call to a command or query, sends it through `IDispatcher` and builds the reply with `.ToGrpcReply(...)`. It holds no business logic: it takes nothing but `IDispatcher`, and the architecture rule `GrpcServicesOnlyDispatch` fails the build otherwise.
- **A failed `Result` is an `RpcException`.** `ToGrpcReply` maps the `ApiError`'s status to gRPC's (400 and 422 `InvalidArgument`, 401 `Unauthenticated`, 403 `PermissionDenied`, 404 `NotFound`, 409 `Aborted`, 412 `FailedPrecondition`, 429 `ResourceExhausted`, any other 4xx `FailedPrecondition`, anything else `Internal`), puts its message in the status detail and its code in the `error-code` trailer, as problem details carry it in `code`.
- **Found by convention.** The host maps every gRPC service of every module's Api assembly (`AddModuleGrpcServices`, `MapModuleGrpcServices`); adding one edits no shared file. Its route is the service's own (`/billing.Invoices/Count`), not under the module's group, because that is where a client calls it.
{{~ if auth ~}}
- **Every service needs a signed-in caller**, as the module routes do: the same bearer token in the `authorization` header, the same authentication. A method that needs a permission names its module's policy with `[Authorize(Policy = ...)]`.
{{~ else ~}}
- **Every service is open**, as every route is without authentication (`backend.auth: none`).
{{~ end ~}}
- **gRPC has an endpoint of its own.** gRPC needs HTTP/2, and Kestrel speaks HTTP/2 without TLS only on an endpoint that speaks nothing else, so the host's settings declare two Kestrel endpoints: `http` (`http://localhost:5080`, as before) and `grpc` (`http://localhost:5081`, `Protocols: Http2`). Aspire turns each into an endpoint of the `host` resource and gives it a port of its own; a client reaches gRPC at the `grpc` endpoint (`App.GetEndpoint("host", "grpc")` in a test).
- **Tests run the real host in process.** `tests/GrpcServices.Tests` serves a sample service beside the modules' and calls it with a generated client through the test server; a module's service gets its tests there too.

## Consequences

- A proto is a published contract: renumbering or retyping a field breaks every client. Add fields, never reuse a number; a breaking change is a new service or package version (`billing.v2`).
- The `grpc` endpoint answers everything the host serves, REST included, over HTTP/2; the `http` endpoint answers gRPC calls only from clients that speak HTTP/2 over TLS, which it does not offer. A deployment that terminates TLS in front of the host can serve both on one port; then the Kestrel settings change, not the code.
- With configured Kestrel endpoints, `ASPNETCORE_URLS` no longer chooses where the host listens: set `Kestrel__Endpoints__http__Url` and `Kestrel__Endpoints__grpc__Url` instead. A host started in process with `WebApplicationFactory` and its test server binds nothing; one started on Kestrel binds the configured ports too.
- What the rule cannot see: whether a service's translation is only a translation (a loop over replies, a computed field), whether a streaming method buffers without limit, and whether a proto's numbers were kept. Those are review's to catch.

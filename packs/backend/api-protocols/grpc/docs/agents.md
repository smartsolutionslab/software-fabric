{{~ auth = project["backend.auth"] != "none" ~}}
## API protocol: gRPC beside REST

- A module's gRPC contract is a `.proto` in its Api project under `Protos/`, with `option csharp_namespace = "{{ namespace }}.Modules.<Module>.Api.GrpcServices";` and `package <module>;`. The build generates the server base and a client from it; never check in generated code or a copy of the proto.
- A gRPC service implements the generated base in the Api project under `GrpcServices/` and takes only `IDispatcher`: translate the request to a command or query, dispatch it, and return `result.ToGrpcReply(value => new <Reply> { ... })`. No business logic, no repository or DbContext; `GrpcServicesOnlyDispatch` (architecture tests) fails the build otherwise.
- Never throw `RpcException` yourself for an expected failure: return a `Failure(...)` with the module's `ApiError`; `ToGrpcReply` maps its status and puts its code in the `error-code` trailer.
- The host finds and maps every service by itself; never call `MapGrpcService` by hand or list a service anywhere.
{{~ if auth ~}}
- Every service requires a signed-in caller. A method that needs a permission takes `[Authorize(Policy = <Module>PolicyNames.<Permission>)]`, from its own module.
{{~ end ~}}
- Never renumber, retype or reuse a field number in a published proto; add a field, or a new package version (`<module>.v2`) for a breaking change.
- Test a service through its generated client in `tests/GrpcServices.Tests`, against `GrpcHost` (the real host in process): `new Invoices.InvoicesClient(host.CreateChannel())`. Under Aspire, gRPC is on the host's `grpc` endpoint (HTTP/2 only), never on `http`.

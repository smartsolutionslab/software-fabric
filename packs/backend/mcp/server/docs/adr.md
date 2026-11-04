{{~ auth = project["backend.auth"] != "none" ~}}
{{~ policies = object.has_key(project, "backend.authorization") && array.size(project["backend.authorization"]) > 0 ~}}
# MCP: server

**Status:** Accepted (generated)

## Decision

The project exposes MCP tools over HTTP at `/mcp`, through the official C# SDK (`ModelContextProtocol.AspNetCore`, streamable HTTP, stateless).

- **A command or query is a tool only when marked** `[McpTool("description")]` on its record. Nothing is exposed by default, and `fabric add-module` marks nothing.
- **A tool is its request.** Its name is `<module>_<request>` in snake case (`billing_find_invoice`), its input schema is the request record's, and a call reads the arguments into the record and sends it through `IDispatcher`, exactly as an endpoint does. The tool holds no logic: a successful `Result` comes back as JSON, a failed one as a tool error starting with the `ApiError`'s code.
{{~ if auth ~}}
- **The endpoint needs a signed-in caller**, as the module routes do: the same bearer token, the same authentication.
{{~ else ~}}
- **The endpoint is open**, as every route is without authentication (`backend.auth: none`): anyone who reaches the host may list and call the tools.
{{~ end ~}}
{{~ if policies ~}}
- **A tool that needs a permission names its module's policy, typed**: `[McpTool("...", Policy = BillingPolicyNames.Manage)]`, from the `<Module>PolicyNames` its Application project declares and its `<Module>Policies` registers. The SDK's authorization filters check it before the call, so a caller without it neither sees the tool in the list nor reaches the handler.
{{~ end ~}}
- **One definition of a valid tool**, `McpToolCatalog` in BuildingBlocks, serves both the host and the build: the host refuses to start on a marked request that is not a valid tool, and the architecture rule `EveryMcpToolIsValid` fails the build on the same problems. A marked record must be a command or a query, have a description and exactly one handler in its module{{ if policies }}, and name only a policy its own module registers{{ end }}.
- **MCP code stays out of the modules.** The attribute and the catalog live in BuildingBlocks, which references no MCP package; only the host references the SDK. A module's Application project references nothing new.
- **Tests never reach an external server or model.** `tests/Mcp.Server.Tests` runs the real host in process with sample requests of its own and calls it with the SDK's client.

## Consequences

- What the rule cannot see: whether a description says what the tool really does; whether a request's arguments can be read from JSON (a value object without a public constructor fails at the call, as a tool error); whether a command is safe to put in a model's hands. Mark a command only when a model calling it with arguments of its own choosing is acceptable{{ if policies }}, and give it a policy when it changes something on someone's behalf{{ end }}.
- The arguments are a caller's input, often a model's: the request's own validation applies to them as to a request body. The tool adds none and skips none.
- A marked record outside a module's Application project is not found: tools come only from modules.
- Renaming a marked request renames its tool, which breaks any client that called it by name.

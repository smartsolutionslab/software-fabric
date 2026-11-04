# MCP: client

**Status:** Accepted (generated)

## Decision

The project uses external MCP servers, through the official C# SDK (`ModelContextProtocol.Core`).

- **The AppHost declares every server**, on the host: `WithMcpServerUrl` for a server reached over HTTP, `WithMcpServerCommand` for one the host starts as a process. The URL or command is an Aspire parameter; a credential (a bearer token, a variable the process needs) is a secret parameter, and a credential declared without `secret: true` fails the AppHost.
- **BuildingBlocks create the clients**, in one place (`AddMcpClients`), from what the AppHost declares; a malformed declaration fails the host at startup. A started server's process inherits only the basic environment (PATH, HOME, ...) and the variables declared for it, never the host's connection strings or another server's secret.
- **Code calls a tool through `IMcpTools`**, whose signatures name no SDK and no `Microsoft.Extensions.AI` type, so a module that uses it still meets every rule about model clients.
- **A chat client gets the tools through `UseMcpTools(allow)`** on its `ChatClientBuilder`: every declared server's tools are offered to the model as `<server>__<tool>`, so two servers' tools of the same name stay apart, and the calls it asks for run through `Microsoft.Extensions.AI`'s function invocation. The `backend.llm` provider that builds the chat client adds it.
- **Every call a model asks for is checked before it runs.** The arguments are the model's output, so `UseMcpTools` takes the project's check (`Func<McpToolCall, bool>`) and has no default: a refused call never reaches the server, and the model is told it was refused. Allowing every call is a decision written at that call site.
- **What a server returns is untrusted input**: a tool's result and its description are the server's text. Code checks a result like a request body before acting on it, never executes it, and never interpolates it into SQL or markup or passes it on as another tool's argument unchecked.
- **Tests never reach a real server.** `tests/Mcp.Tests` declares a tiny server of its own in the AppHost's model, over HTTP and over stdio, and calls it with a stand-in model.

## Consequences

- An architecture rule fails the build when a module references the MCP SDK: a client created there would bypass the declaration, its credentials and the checks.
- A check that allows every call lets a tool run with whatever arguments the model chose, including text another server returned. Allow that only for tools that change nothing on the project's behalf.
- A tool name the request already offers, next to a server's `<server>__<tool>`, fails the request: the model could not tell the two apart.
- Adding a server is one line in the AppHost and its parameter values in user secrets or the deployment's secret store; no module changes.

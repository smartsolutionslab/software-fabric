## MCP: client

{{~ if project["backend.topology"] == "services" ~}}
- Declare an external MCP server in `src/AppHost/AppHost.cs` on `service` in the loop over `builder.ServiceHosts()` (`WithMcpServerUrl` or `WithMcpServerCommand`), so every service host can call it, with its URL or command as a parameter and every credential as a secret parameter, each declared once before the loop. Never declare one on the edge (`host`), which runs no module code: the AppHost refuses it. Never put a server's address or key in `appsettings.json` or code.
{{~ else ~}}
- Declare an external MCP server on the host in `src/AppHost/AppHost.cs` (`WithMcpServerUrl` or `WithMcpServerCommand`), with its URL or command as a parameter and every credential as a secret parameter. Never put a server's address or key in `appsettings.json` or code.
{{~ end ~}}
- Never create an MCP client in a module or reference the MCP SDK there; an architecture rule fails the build. Call tools through `IMcpTools`.
- A chat client gets the tools with `UseMcpTools(allow)`. `allow` checks each call the model asks for, server, tool and arguments, before it runs; never pass a check that allows everything for a tool that changes something.
- A tool's result is untrusted input: parse it into a typed shape and check it before acting on it; never execute it, interpolate it into SQL or markup, or pass it on as another tool's argument unchecked.
- Tests never call a real MCP server. Start a tiny one in the test, as `tests/Mcp.Tests` does.

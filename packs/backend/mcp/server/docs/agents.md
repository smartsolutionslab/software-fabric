{{~ auth = project["backend.auth"] != "none" ~}}
{{~ policies = object.has_key(project, "backend.authorization") && array.size(project["backend.authorization"]) > 0 ~}}
## MCP: server

- To expose a use case to MCP clients, mark its command or query record with `[McpTool("what it does, for the model")]` (`{{ namespace }}.BuildingBlocks.Mcp`). Nothing else: the host offers it as `<module>_<request>` in snake case, with the record as its input schema, and calls the handler through `IDispatcher`. Never write a tool class, an endpoint or a handler for MCP.
- Mark only what a model may call with arguments of its own choosing. The handler validates them as it would a request body.
{{~ if policies ~}}
- A tool that needs a permission names its module's policy from `<Module>PolicyNames`: `[McpTool("...", Policy = BillingPolicyNames.Manage)]`. Never another module's, and never a string literal. A caller without it neither sees nor reaches the tool.
{{~ end ~}}
- A marked record without a description, without exactly one handler{{ if policies }}, or naming a policy its module does not register{{ end }} fails the architecture rule `EveryMcpToolIsValid`, and would stop the host.
- Never reference the MCP SDK from a module; only the host does{{ if auth }}. The endpoint `/mcp` requires a signed-in caller, as the module routes do{{ end }}.
- A new tool gets a test through the SDK's client, as `tests/Mcp.Server.Tests` does: listed, called, refused{{ if policies }} without its policy{{ end }}.

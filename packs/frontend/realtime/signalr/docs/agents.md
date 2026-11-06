{{~ tenanted = project["backend.tenancy"] != "none" ~}}
## Realtime: SignalR

- To push to the browser, send through `IBrowserEvents` (`{{ namespace }}.BuildingBlocks.Realtime`): `await browserEvents.SendAsync(message, cancellationToken)`. Never inject `IHubContext` or add a hub; `/hubs/events` is the only one.
- To show another module's integration event in the browser, forward it from a consumer{{ if tenanted }} inside `using var tenant = TenantScope.Begin(message.Metadata.TenantId);`: an event reaches the browsers of the current tenant only{{ end }}.
{{~ if project["frontend.framework"] == "vue" ~}}
- In a component's setup, listen with `useServerEvent<InvoicePaidV1>('InvoicePaidV1', handler)` from `@/realtime/useServerEvent`, the name being the event's type; it stops listening when the component unmounts. Then refetch the data the event is about rather than building it from the event.
- Never create a `HubConnection` yourself; `src/realtime/ServerEvents.vue` holds the app's one connection.
{{~ else ~}}
- In a component, listen with `useServerEvent<InvoicePaidV1>('InvoicePaidV1', handler)` from `@/realtime/useServerEvent`, the name being the event's type; pass a stable handler (`useCallback`). Then refetch the data the event is about rather than building it from the event.
- Never create a `HubConnection` yourself; `src/realtime/ServerEvents.tsx` holds the app's one connection.
{{~ end ~}}
- In Development, `POST /hubs/events/test` with `{ "text": "..." }` sends a `TestEvent` to {{ if tenanted }}your tenant's{{ else }}every{{ end }} browser{{ if tenanted }}s{{ end }}.

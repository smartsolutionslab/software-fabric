{{~ tenanted = project["backend.tenancy"] != "none" ~}}
## Realtime: server-sent events

- To push to the browser, send through `IBrowserEvents` (`{{ namespace }}.BuildingBlocks.Realtime`): `await browserEvents.SendAsync(message, cancellationToken)`. Never write to a response stream yourself or add another stream; `/server-events` is the only one.
- To show another module's integration event in the browser, forward it from a consumer{{ if tenanted }} inside `using var tenant = TenantScope.Begin(message.Metadata.TenantId);`: an event reaches the browsers of the current tenant only{{ end }}.
{{~ if project["frontend.framework"] == "vue" ~}}
- In a component's setup, listen with `useServerEvent<InvoicePaidV1>('InvoicePaidV1', handler)` from `@/realtime/useServerEvent`, the name being the event's type; it stops listening when the component unmounts. Then refetch the data the event is about rather than building it from the event.
- Never create an `EventSource` yourself; `src/realtime/ServerEvents.vue` holds the app's one stream.
{{~ else ~}}
- In a component, listen with `useServerEvent<InvoicePaidV1>('InvoicePaidV1', handler)` from `@/realtime/useServerEvent`, the name being the event's type; pass a stable handler (`useCallback`). Then refetch the data the event is about rather than building it from the event.
- Never create an `EventSource` yourself; `src/realtime/ServerEvents.tsx` holds the app's one stream.
{{~ end ~}}
{{~ if project["backend.topology"] == "services" ~}}
- With services, the stream (`/server-events`) is the edge's (`src/Edge`), which every browser is connected to: a service's `IBrowserEvents` publishes a `BrowserEventV1` on the broker, and the edge's `BrowserEventConsumer` pushes it to its browsers. Never map a stream in a service or send `BrowserEventV1` yourself; V1 runs one edge replica, so never scale the edge out.
- In Development, each service's `POST /<module>/server-events/test` with `{ "text": "..." }` sends a `TestEvent` from that service through the edge to every browser.
{{~ else ~}}
- In Development, `POST /server-events/test` with `{ "text": "..." }` sends a `TestEvent` to {{ if tenanted }}your tenant's{{ else }}every{{ end }} browser{{ if tenanted }}s{{ end }}.
{{~ end ~}}

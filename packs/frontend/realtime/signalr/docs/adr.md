{{~ tenanted = project["backend.tenancy"] != "none"
   signed_in = project["backend.auth"] != "none"
   test = project["frontend.framework"] == "vue" ? "ts" : "tsx" ~}}
# Realtime: SignalR

**Status:** Accepted (generated)

## Decision

The host pushes events to the browsers over ASP.NET Core SignalR. There is one hub, `/hubs/events` (`src/Host/Realtime/EventsHub.cs`); browsers only listen on it. {{ if signed_in }}It requires a signed-in user, like every other route.{{ else }}It is open to anyone, like every other route (`backend.auth: none`).{{ end }}{{ if tenanted }} A connection joins its user's tenant group, so a browser receives its own tenant's events and no other's.{{ end }}

Server code sends through `IBrowserEvents` (`src/BuildingBlocks/Realtime`), never through the hub itself: `SendAsync(event, cancellationToken)` sends {{ if tenanted }}to the current tenant's browsers, and throws without a tenant{{ else }}to every connected browser{{ end }}. The event's type name is what the browser listens for. A module shows an integration event in the browser by forwarding it from a consumer.

The browser connects with `@microsoft/signalr` through `/api`, like every other call{{ if signed_in }}, and the client takes the user's access token from `accessTokenFactory`. A browser cannot set a header on a WebSocket or an EventSource, so SignalR sends the token as the `access_token` query parameter there; the host's bearer handler reads it for the hub's path only{{ end }}. `ServerEvents` (`src/realtime/`) holds one connection for the app, and components listen with `useServerEvent(name, handler)`. SignalR picks the transport itself (WebSockets, then server-sent events, then long polling) and reconnects after a drop; `ServerEvents` starts the connection again when a first start fails or the reconnects run out, for as long as it is mounted.{{ if signed_in }} The host closes a connection once its access token expires, so the browser reconnects with a fresh token and the user and their tenant are checked again.{{ end }}

In Development, `POST /hubs/events/test` with `{ "text": "..." }` sends a `TestEvent` the same way, so the push channel can be seen working; it is not mapped elsewhere and is not in the OpenAPI document.

## Gates

- `tests/Integration.Tests/ServerEventsTests.cs`, against the running application: {{ if tenanted }}a browser of the sender's tenant receives a test event and one of another tenant does not{{ else }}a connected browser receives a test event{{ end }}{{ if signed_in }}; a connection without a token is refused, and one whose token expires is closed{{ end }}.
- `src/realtime/useServerEvent.test.{{ test }}` and `ServerEvents.test.{{ test }}`: a component receives the events it listens for and stops when it unmounts; a connection that fails to start or closes for good is started again while mounted.
- `e2e/server-events.spec.ts`: the {{ if signed_in }}signed-in {{ end }}browser's own connection receives a test event.

## Consequences

- The hub holds a connection per open tab. One host instance needs nothing more; several need a backplane (Redis, or Azure SignalR Service) so that every instance reaches every browser.
- A browser that is not connected when an event is sent never sees it. Events say that something changed; a screen fetches the data again rather than trusting the event to carry all of it.

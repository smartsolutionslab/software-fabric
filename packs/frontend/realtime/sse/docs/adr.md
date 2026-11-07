{{~ tenanted = project["backend.tenancy"] != "none"
   signed_in = project["backend.auth"] != "none"
   vue = project["frontend.framework"] == "vue"
   test = vue ? "ts" : "tsx" ~}}
# Realtime: server-sent events

**Status:** Accepted (generated)

## Decision

The host pushes events to the browsers as server-sent events: one stream, `GET /server-events` (`src/Host/Wiring/ServerEventsEndpoints.cs`), which ASP.NET Core writes with `TypedResults.ServerSentEvents`; browsers only listen on it. {{ if signed_in }}It requires a signed-in user, like every other route.{{ else }}It is open to anyone, like every other route (`backend.auth: none`).{{ end }}{{ if tenanted }} A stream belongs to its user's tenant, so a browser receives its own tenant's events and no other's.{{ end }}

Server code sends through `IBrowserEvents` (`src/BuildingBlocks/Realtime`), never to the streams themselves: `SendAsync(event, cancellationToken)` sends {{ if tenanted }}to the current tenant's browsers, and throws without a tenant{{ else }}to every open stream{{ end }}. The event's type name is the event's name on the stream, and what the browser listens for; its data is the event as JSON. A module shows an integration event in the browser by forwarding it from a consumer.

The browser opens the stream with its own `EventSource` through `/api`, like every other call; nothing is installed for it. `src/realtime/connection.ts` holds one for the app, `ServerEvents` (`src/realtime/ServerEvents.{{ if vue }}vue{{ else }}tsx{{ end }}`) keeps it open while the app is mounted, and components listen with `useServerEvent(name, handler)`. A stream that fails is closed and opened anew, after a second, doubling up to thirty, rather than left to the EventSource's own retry, which would reuse its URL.{{ if signed_in }} An `EventSource` cannot send a header, so the client puts the user's access token in the `access_token` query parameter, read afresh at every opening, and the host's bearer handler reads it there for `/server-events` only. The host ends a stream once its access token expires, so the browser opens it again with a fresh token and the user and their tenant are checked again.{{ end }}

In Development, `POST /server-events/test` with `{ "text": "..." }` sends a `TestEvent` the same way, so the stream can be seen working; it is not mapped elsewhere, and neither route is in the OpenAPI document.

## Gates

- `tests/Integration.Tests/ServerEventsTests.cs`, against the running application: {{ if tenanted }}a browser of the sender's tenant receives a test event and one of another tenant does not{{ else }}an open stream receives a test event{{ end }}{{ if signed_in }}; a stream without a token is refused, and one whose token expires is ended{{ end }}.
- `src/realtime/connection.test.ts`: a listener receives the events of its name until it stops listening, and a failed stream is opened anew{{ if signed_in }} with the current token{{ end }} until it is stopped.
- `src/realtime/useServerEvent.test.{{ test }}` and `ServerEvents.test.{{ test }}`: a component receives the events it listens for and stops when it unmounts; the stream is open while the app is mounted.
- `e2e/server-events.spec.ts`: the {{ if signed_in }}signed-in {{ end }}browser's own stream receives a test event.

## Consequences

- The host holds a response open per tab, and keeps the streams in its memory. One host instance needs nothing more; several need a shared channel (Redis pub/sub, or the message broker) so that every instance reaches every browser.
- A stream that falls 100 events behind loses its oldest ones. A browser that is not connected when an event is sent never sees it. Events say that something changed; a screen fetches the data again rather than trusting the event to carry all of it.
- Over HTTP/1.1 a browser opens at most six connections to a host, and each tab's stream takes one; HTTP/2 lifts that limit.{{ if signed_in }}
- The access token is in the stream's URL, so it can appear in access logs; the host reads it for `/server-events` only, and it lives only as long as the token.{{ end }}

## Authentication: none

- The project has no authentication: every endpoint is anonymous by choice (an internal tool, a demo, or a service behind a gateway that authenticates). There is no `/me` and no sign-in.
- Do not add authentication by hand: no `AddAuthentication`, no JWT bearer or cookie setup, no `RequireAuthorization()`, no token handling in the frontend. The architecture rule `NoAuthentication` fails the build for an endpoint that requires authorization or a scheme the host registers, naming it.
- A feature that needs to know who the caller is needs an identity provider: escalate it with `needs:decision` (choose an option for `backend.auth`) instead of building one.

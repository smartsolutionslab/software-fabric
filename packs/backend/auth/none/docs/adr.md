# Authentication: none

**Status:** Accepted (generated)

## Decision

The project authenticates nobody. There is no identity provider in the AppHost, the host validates no tokens and has no `/me`, module route groups require no authorization, and the web app has no auth client and no sign-in. Every endpoint is anonymous, by choice: this is for an internal tool, a demo, or a service behind a gateway that authenticates for it.

The architecture rule `NoAuthentication` keeps it that way. It starts the host in-process twice, in Development and in Production (`WebApplicationFactory`, every hosted service removed, placeholder connection strings), and fails for every endpoint that requires authorization (`RequireAuthorization()` or a policy, which would fail at runtime without authentication) and for every authentication scheme the host registers, naming each one.

In place of the auth client's sign-in test, the web app's end-to-end tests start with `e2e/home.spec.ts`: an anonymous visitor opens the application and sees it, so the generated CI's e2e step has a test to run.

## Consequences

- Anyone who can reach the host can call every endpoint. Whatever stands in front of it (a gateway, a private network) is the only protection, and it is outside this project.
- Authentication is not added by hand, module by module. A project that needs it chooses an identity provider for `backend.auth` (and, with it, `backend.authorization`), which brings token validation, the sign-in flow and its own rules in one change.
- Options that need a signed-in user cannot be combined with it: column tenancy, which reads the tenant from the user's token, fails the build saying so.

# Integration tests: the Aspire application under test

**Status:** Accepted (generated)

## Decision

Integration tests start the real application — the AppHost with its containers and the host — through `Aspire.Hosting.Testing`, once per test run (`AspireFixture`), and talk to it over HTTP like any client. Containers start empty: the fixture drops data volumes, so a test never sees a developer's data and never depends on it.

The generated tests check what each choice promises at runtime: every resource becomes healthy, the host's `/health` is healthy, the OpenAPI document is served, anonymous calls are rejected and a token from the realm is accepted, each user resolves to their own tenant.

## Consequences

- The tests need Docker, locally and in CI. The first run pulls the images.
- A module's own behaviour is tested against the same fixture: add a test class taking `AspireFixture` to `tests/Integration.Tests/`.

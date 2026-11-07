# Integration tests: the application on Testcontainers

**Status:** Accepted (generated)

## Decision

Integration tests start the application's resources (the database, the broker, the identity provider) in containers that [Testcontainers](https://dotnet.testcontainers.org/) runs, and the host in the test process with `WebApplicationFactory`, on a port of its own (Kestrel, so WebSockets and every other client work as against a deployed host). One fixture, `ContainerFixture`, starts them once per test run, empty, and gives the host the settings the AppHost would give it: the connection strings, the identity provider's authority, the model providers' parameters. The MigrationRunner runs to its end before the host starts, as under the AppHost. The tests talk to the host over HTTP like any client.

The AppHost stays the composition root: it is what runs the application, locally and in the end-to-end tests. The tests do not start it: each resource's pack brings its container for the tests beside its AppHost resource, and both start the same images.

The generated tests check what each choice promises at runtime: the host's `/health` is healthy, the OpenAPI document is served, anonymous calls are rejected and a token from the identity provider is accepted, each user resolves to their own tenant.

Testcontainers is MIT-licensed; its packages are pinned in `Directory.Packages.props` (4.16.0).

## Consequences

- The tests need Docker, locally and in CI; the first run pulls the images, and Testcontainers' resource reaper (Ryuk) removes the containers if a run is killed.
- Build before testing (`dotnet build build.proj`): the fixture runs the built MigrationRunner (`dotnet run --no-build`) in the tests' configuration.
- Setup gives up after five minutes, or after the time span in `INTEGRATION_TESTS_STARTUP_TIMEOUT` (`00:01:30`), and kills what it started with `dotnet run` and every process under it: a MigrationRunner that stalls does not outlive the run.
- A resource the AppHost adds needs a container here too, or the host starts without it: the resource's pack contributes both. What the AppHost starts only for people (Mailpit for Keycloak's email, the Kafka and RabbitMQ consoles) is not started, so Keycloak's email is not tested here.
- A module's own behaviour is tested against the same fixture: add a test class taking `ContainerFixture` to `tests/Integration.Tests/`.

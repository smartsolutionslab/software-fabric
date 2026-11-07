## Integration tests: Testcontainers

- `tests/Integration.Tests` starts the application's resources in containers with Testcontainers and the host in process (Docker required); build first with `dotnet build build.proj`. Take `ContainerFixture` in a test class's constructor; it is started once per run.
- Call the host with `containers.CreateHostClient()`, or `await containers.CreateHostClientAsync("developer")` for a signed-in user (`developer` is tenant-a, `developer-b` tenant-b).
- A new resource in the AppHost needs its container in the fixture as well, with the settings the AppHost gives the host; never point a test at a developer's running containers.
- An integration test proves behaviour end to end; prefer it for anything that crosses the database, the broker or authentication.

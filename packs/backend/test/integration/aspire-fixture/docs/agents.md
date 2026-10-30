## Integration tests: Aspire

- `tests/Integration.Tests` runs the whole application through Aspire (Docker required). Take `AspireFixture` in a test class's constructor; it is started once per run.
- Call the host with `aspire.CreateHostClient()`, or `await aspire.CreateHostClientAsync("developer")` for a signed-in user (`developer` is tenant-a, `developer-b` tenant-b).
- An integration test proves behaviour end to end; prefer it for anything that crosses the database, the broker or authentication.

## Message transport: none (in-process outbox)

- There is no broker. Publish integration events through `I<Module>UnitOfWork` exactly as with one: `PublishAsync(...)`, then `CommitAsync(...)`. The events are saved with the changes in the module's `outbox_messages` table and delivered in-process after the commit.
- A consumer is a public class `<Event>Consumer` in a module's Application project with a public `Handle(<Event> message)` returning `void` or `Task`. Further `Handle` parameters may be a `CancellationToken` or services; constructor dependencies are injected too. An architecture rule checks the shape.
- Do not call another module's code or consumer directly, and do not add a broker, a messaging library or a background queue by hand: the transport is a project choice.
- The MigrationRunner creates each module's outbox table; do not map it in a DbContext, add it to a migration, or create it from the host. A test that uses a unit of work runs the MigrationRunner first.

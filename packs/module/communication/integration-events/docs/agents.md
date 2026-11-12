{{~
  # shared_records is the tenant-column pack's; no other tenancy defines it.
  shared = false
  if project["backend.tenancy"] == "column"
    shared = shared_records
  end
~}}
## Module communication: integration events

- To tell other modules something happened, add a record `<Something>V1 : IntegrationEvent` under `src/Contracts/<Module>/`, publish it with `I<Module>UnitOfWork.PublishAsync(...)` and then `CommitAsync(...)`. Never publish outside the unit of work.
- Never set an event's `Metadata` yourself: publishing fills it with a Guid v7 `EventIdentifier` and the `OccurredAt` time{{ if shared }}, and the current `TenantId` (publishing without a tenant fails, except inside `WritesSharedRecords()`, where the `TenantId` is null){{ else if project["backend.tenancy"] != "none" }}, and the current `TenantId` (publishing without a tenant fails){{ end }}. An architecture rule fails an event that does not derive from `IntegrationEvent`.
- To react to another module's event, add `<Event>Consumer` with `Handle(<Event> message)` (or `async Task HandleAsync(<Event> message)` when it awaits) in your module's Application project. Consumers are idempotent: the same event may arrive twice, with the same `message.Metadata.EventIdentifier`; deduplicate on it.
- Never edit a published event; add `V2` beside it.
- Name consumers `...Consumer`, never `...Handler`: handlers are for your own commands and queries.

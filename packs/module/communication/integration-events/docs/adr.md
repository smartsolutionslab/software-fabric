{{~
  # shared_records is the tenant-column pack's; no other tenancy defines it.
  shared = false
  if project["backend.tenancy"] == "column"
    shared = shared_records
  end
~}}
# Module communication: integration events

**Status:** Accepted (generated)

## Decision

Modules tell each other what happened through integration events: records in `src/Contracts/<Module>/`, deriving from `IntegrationEvent`, named in the past tense with a version suffix (`InvoicePaidV1`). An event is published through the module's unit of work (`I<Module>UnitOfWork.PublishAsync`) and sent only when `CommitAsync` succeeds, so a change and the event announcing it are saved together or not at all.

Every event carries `EventMetadata`: a Guid v7 `EventIdentifier` and the `OccurredAt` time{{ if shared }}, and the `TenantId` of the work that published it, null for an event published where the records shared by all tenants are written (`WritesSharedRecords()`){{ else if project["backend.tenancy"] != "none" }}, and the `TenantId` of the work that published it{{ end }}. The unit of work fills it when the event is published, before it is saved, so code that creates an event cannot forget or fake it, and every delivery of the event carries the same metadata.

Another module reacts with a consumer — a class named `<Event>Consumer` in its Application project with a `Handle(<Event>)` method, or `HandleAsync(<Event>)` when it awaits.

## Consequences

- An event's shape is a contract: change it by adding `<Event>V2`, never by editing V1 in place.
- A consumer runs later and possibly more than once; it must be idempotent, and `Metadata.EventIdentifier` is what it deduplicates on.
- An event read before it was published has no metadata; reading `Metadata` then throws.

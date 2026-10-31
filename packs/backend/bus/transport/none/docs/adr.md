# Message transport: none (in-process, through an outbox table)

**Status:** Accepted (generated)

## Decision

There is no broker and no messaging library. Modules still talk through integration events, but the events never leave the host process.

- `I<Module>UnitOfWork.CommitAsync` saves the module's changes and its queued events in one database transaction: the events become rows of `outbox_messages` in the module's own schema.
- After the commit, a dispatcher in the host reads each module's outbox, hands every message to the consumers of its type (classes named `*Consumer` in the modules' Application projects, with `Handle(<event>)`), and marks it delivered. Rows are locked with `for update skip locked`, so several host instances do not deliver the same message at once.
- A consumer that throws leaves its message undelivered; it is retried, up to ten attempts, and the last error is kept in the row (`last_error`).
- The outbox table is messaging storage, like a messaging library's envelope tables: the host creates it on startup, it is not part of the module's EF Core model, and no migration mentions it.

## Consequences

- No event is lost when a consumer fails or the host stops between commit and delivery: it waits in the table.
- Delivery is at least once, and a message with several consumers is retried for all of them; consumers must be idempotent.
- Moving to a broker later (`backend.bus.transport`) keeps the contract modules use, `I<Module>UnitOfWork` and `*Consumer`, and replaces only this delivery; it is a project migration with its own ADR.
- Delivered rows stay in the table. Removing old ones is an operational task, not yet automated.

# Message transport: none (in-process, through an outbox table)

**Status:** Accepted (generated)

## Decision

There is no broker and no messaging library. Modules still talk through integration events, but the events never leave the host process.

- `I<Module>UnitOfWork.CommitAsync` saves the module's changes and its queued events in one database transaction, which it opens itself: the events become rows of `outbox_messages` in the module's own schema. It refuses to run inside a transaction a caller began, because it cannot tell whether that one commits. The changes count as saved only once the transaction has committed, so a commit that failed can be retried with the same unit of work.
- After the commit, a dispatcher in the host reads each module's outbox, hands every message to the consumers of its type (classes named `*Consumer` in the modules' Application projects, with `Handle(<event>)`), and marks it delivered. Rows are locked with `for update skip locked`, so several host instances do not deliver the same message at once.
- A consumer that throws leaves its message undelivered, and the message backs off before it is tried again, counted from the moment it failed: 1 second after the first failure, doubling with every further one (1, 2, 4, … 256 seconds), never more than 5 minutes. The wait is kept in the row (`next_attempt_at`), so it holds however busy the dispatcher is, and other messages are delivered meanwhile. After ten attempts, about eight and a half minutes after the first, the message is left for a developer with its last error in `last_error`.
- The dispatcher reads up to 20 messages at a time in one transaction, and that transaction, with the messages' row locks, stays open while the batch's consumers run. A slow consumer therefore keeps a database connection and its batch's rows locked until it returns; other host instances skip those rows rather than wait for them.
- The outbox table is not part of the module's EF Core model, so no EF Core migration mentions it. The MigrationRunner creates it in every module's schema, as a step after the modules' migrations; the host never creates tables, so hosts starting together do not race over it.

## Consequences

- No event is lost when a consumer fails or the host stops between commit and delivery: it waits in the table.
- Delivery is at least once, and a message with several consumers is retried for all of them; consumers must be idempotent.
- Moving to a broker later (`backend.bus.transport`) keeps the contract modules use, `I<Module>UnitOfWork` and `*Consumer`, and replaces only this delivery; it is a project migration with its own ADR.
- Delivered rows stay in the table. Removing old ones is an operational task, not yet automated.

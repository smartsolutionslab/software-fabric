# CQRS: physical

**Status:** Accepted (generated)

## Decision

Writes and reads use different models and different storage. Commands (`Commands/` in the Application project) load and save aggregates through the module's aggregate store. Queries (`Queries/`) never touch them: they read read models, plain classes under the Application project's `ReadModels/`, through the module's `I<Module>ReadModels`.

Read models are fed by projections from the module's events. A projection is a class in the module's Infrastructure project; the event store runs it inline, so a read model is current as soon as the command that changed it returns. Each read model is its own table in the module's schema, apart from the event streams.

Built for event-sourced modules on Marten. Generating it for any other combination fails: there is no event stream to project from yet.

## Consequences

- A query can have exactly the shape a screen needs, and adding one never changes the write model.
- A new read model, or a changed projection, needs its table rebuilt from the events; Marten's projection rebuild does that, but it is not wired into a command or the migration runner yet.
- An architecture rule fails the build when anything in a Queries namespace of a module that chose physical CQRS depends on an aggregate store, an aggregate or a DbContext. Modules with another CQRS choice are left to their own rules.

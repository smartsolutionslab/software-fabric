# Module communication: contract interfaces

**Status:** Accepted (generated)

## Context

A module sometimes needs an answer from another module now, inside the request it is serving: a total, a status, whether something exists. Integration events tell other modules what happened, later; they cannot answer a question. Referencing the other module's projects would answer it, and would tie the two modules together, so it is not allowed.

## Decision

A module answers other modules through an interface in Contracts, which every module may reference:

- **The interface** is `I<Module>Api` in `src/Contracts/<Module>/` (`Contracts.<Module>` namespace). Its parameters and results are Contracts or Kernel types, never a module's own; types it needs beyond those live beside it.
- **The implementation** is `<Module>Api` in the providing module's Api project, its entry-point layer beside its endpoints. Each method dispatches one of the module's own queries or commands and holds no business logic, as an endpoint does.
- **Registration is by convention.** Every module's infrastructure registers, scoped, each class in its Api project that implements a module API. No shared file has a line per module or per API.
- **A caller** takes the other module's `I<Module>Api` in a handler's constructor and calls it in-process.
- `fabric add-module` gives every module that chooses this option an empty `I<Module>Api` and `<Module>Api`; the module adds methods to both.

An architecture rule fails the build when a module API is outside `Contracts.<Module>`, when it has no implementation or more than one, or when a module implements another module's API. The rule that no module references another module's projects is unchanged: a call goes through Contracts, so two modules may call each other.

## Consequences

- A call is synchronous and in-process: it runs in the caller's request and dependency-injection scope{{ if project["backend.tenancy"] != "none" }}, so for the caller's tenant{{ end }}, and fails when the other module fails.
- A call does not join the caller's transaction: the other module saves in its own context. Telling another module that something changed stays an integration event; the API is for asking.
- The option is chosen beside `integration-events`, never instead of it, and the catalog refuses it alone: the unit of work and the messaging wiring every module gets come from that pack.
- An API is a contract between modules: change a method by adding a new one beside it, never by changing its signature while another module calls it.
- Each module's own test starts that module alone. An API of another module that it takes is a stand-in there, which throws if called; a call across modules is tested against the running host.

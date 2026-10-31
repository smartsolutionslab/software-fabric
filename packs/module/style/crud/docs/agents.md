## Module style: CRUD

- Entities are plain classes in the module's Domain project, one file each: an identifier and properties. No aggregate roots, domain events or entity base classes.
- Map each entity in Infrastructure with an `IEntityTypeConfiguration<T>` under `Persistence/Configurations/`; never put EF Core or ASP.NET Core attributes on an entity. An architecture rule fails the build if the Domain project references either.
- Validate the request in the use case before creating or changing an entity; there are no invariants hidden inside the entity.
- A module whose rules outgrow this (invariants across entities, restricted state changes) needs another module style; escalate with `needs:decision`.

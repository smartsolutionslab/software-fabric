## DDD depth: strict

- No primitives in this module's aggregates (and the entities and value objects reachable from them), commands or domain events: no keyword type (`string`, `int`, `bool`, `decimal`, ...) and no `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` or `Uri`, not even inside a collection. A public field counts as much as a property. An architecture rule names each one. Queries and DTOs may use primitives.
- A value object is a sealed record implementing `IValueObject<T>` with a private constructor and `public static T From(T value)` that guards the value; copy `Domain/Samples/SampleName.cs`.
- An identity is a sealed record implementing `IValueObject<Guid>`, created with `New()` (a Guid v7) or `From(Guid)`, which refuses an empty Guid; copy `Domain/Samples/SampleId.cs`. The samples are there to be renamed or deleted.
- Never write `HasConversion` for a value object or identifier: EF Core maps them by convention. Map a value object with more than one value as a complex property.
- An endpoint turns the request's primitives into value objects with `From` before it builds the command.
- Aggregate roots expose no public setters; state changes go through methods named after what happens. An architecture rule checks this.

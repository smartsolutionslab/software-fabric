# DDD depth: strict

**Status:** Accepted (generated)

## Decision

No primitives on the domain model. Every value with meaning is a value object: a sealed record implementing
`IValueObject<T>` (`src/Kernel`), created only through `public static T From(T value)`, which guards it. Identities are
strongly typed identifiers over a Guid v7 (`InvoiceId.New()`), and an empty Guid is never one.

**What the rule covers.** A module that chose strict keeps primitives out of:

- its aggregates, and the entities and value objects reachable from them;
- its commands;
- its domain events.

Queries and DTOs are not covered: DTOs are the JSON boundary, where primitives belong, and a query carries the values a
caller sends.

**What counts as a primitive** is a category, not a list someone has to remember: every type with a C# keyword
(`bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`, `nuint`, `char`, `float`,
`double`, `decimal`, `string`, `object`) and the BCL types that carry no domain meaning of their own: `Guid`,
`DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` and `Uri`. A primitive inside a collection, an array or
a `Nullable<T>` counts too.

**Exemptions.** A value object holds its own backing values (`InvoiceNumber.Value` is a `string`). A computed `bool`
without a setter or backing field (`IsPaid => PaidAt is not null`) is an answer, not state; no other get-only property
is exempt, because reflection cannot tell a computed number from stored state.

**Persistence.** EF Core maps every `IValueObject<T>` of a module's Domain to its backing value by convention
(`ConfigureConventions` in `ModuleDbContext`) and reads it back through `From`, so entity configurations never convert
one by hand. A value object with more than one value (`Money`) is mapped in its configuration as a complex property.

## Enforced

An architecture rule per strict module walks from its aggregates, commands and domain events and fails on each
primitive it reaches, naming the type and property.

## Consequences

- More types, and an endpoint converts a request's primitives into value objects before it builds a command.
- A value read from the database passes the same guards as one a caller sends.

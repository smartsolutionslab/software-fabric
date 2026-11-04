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

Private fields are not covered either. Strict guards the state a model exposes, through public properties and public
fields; a private field is implementation.

**What counts as a primitive** is a category, not a list someone has to remember: every type with a C# keyword
(`bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`, `nuint`, `char`, `float`,
`double`, `decimal`, `string`, `object`) and the BCL types that carry no domain meaning of their own: `Guid`,
`DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` and `Uri`. A primitive inside a collection, an array or
a `Nullable<T>` counts too.

**Exemptions.** A value object holds its own backing values (`InvoiceNumber.Value` is a `string`), a generic one too: a
`Positive<int>` property holds a value object, not an `int`. An aggregate, command or domain event is never a value
object for this purpose, even if it implements `IValueObject`. A computed `bool` without a setter
(`IsPaid => PaidAt is not null`) is an answer, not state, unless its getter only returns a field
(`IsArchived => archived`), which is stored state. No other get-only property is exempt, because reflection cannot tell
a computed number from stored state.

**Persistence.** EF Core maps every `IValueObject<T>` of a module's Domain to its backing value by convention
(`ConfigureConventions` in `ModuleDbContext`) and reads it back through `From`, so entity configurations never convert
one by hand. A generic value object (`TypedId<TTag>`) is mapped as each closed type the Domain's properties and fields
use (`TypedId<Customer>`). A value object with more than one value (`Money`) is mapped in its configuration as a complex
property.

{{~ if object.has_key(slots, "event-store.json-converters") ~}}
**Event store.** An event-sourced module may be strict too. Marten serialises its events and documents with
System.Text.Json, and a converter registered with every module's store writes a value object as its backing value and
reads it back through `From`, so no value object carries a `[JsonConverter]` attribute and a value read from the store
passes the same guards as one a caller sends. An event-sourced aggregate's identity is a value object over a `Guid` that
is also its `IAggregateId`. A JSON `null` for a value object that is not declared nullable fails to read
(`RespectNullableAnnotations`), so it never skips `From`; one declared nullable (`InvoiceNumber?`) may be null.

**Not yet in event-sourced modules: value objects over several values.** The converter writes a value object as its one
backing value, so `Money` (an amount and a currency) cannot be stored in an event. An architecture rule per strict
event-sourced module refuses a value object that is not an `IValueObject<T>` for exactly one `T`, until a JSON shape for
several values is decided; then the rule can be lifted.
{{~ end ~}}

## Enforced

An architecture rule per strict module walks from its aggregates, commands and domain events, through public
properties and public fields, and fails on each primitive it reaches, naming the type and member. A second rule per
strict module fails on each public setter of an aggregate root: strict is light and more.

## Consequences

- More types, and an endpoint converts a request's primitives into value objects before it builds a command.
- A value read from the database passes the same guards as one a caller sends.

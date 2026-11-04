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

**An enum is allowed**, the module's own or the BCL's, alone, nullable or in a collection: it is a named set of values,
not a primitive held as state. The walk does not look into one, so the `value__` field the compiler gives every enum is
never reported.

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

**Scope of the option.** Strict applies to `module.style: ddd` only. An event-sourced module stays light for now: its
events go through Marten's JSON, which cannot build a value object through its private constructor and `From`.

## Enforced

An architecture rule per strict module walks from its aggregates, commands and domain events, through public
properties and public fields, and fails on each primitive it reaches, naming the type and member. A second rule per
strict module fails on each public setter of an aggregate root: strict is light and more.

The walk visits each closed type once, and does not enter a closed type larger than the nearest closed type of the same
generic definition it is already inside of: `Node<T>` holding a `Node<List<T>>` would otherwise construct a new type at
every step and never end. A closed type of the same size or smaller is entered as usual, so `Wrapper<Wrapper<Cargo>>`
still reaches `Cargo`, and the walk is what it was wherever it ended before. Of a type it does not enter, it still
reaches what the arguments the definition holds as state bring in (`Wrapper<T>` holds its `T`), and nothing a phantom
tag names: `TypedId<TTag>` holds a `Guid`, not its tag, so `TypedId<Customer>` never makes the walk look into
`Customer`.

## Consequences

- More types, and an endpoint converts a request's primitives into value objects before it builds a command.
- A value read from the database passes the same guards as one a caller sends.

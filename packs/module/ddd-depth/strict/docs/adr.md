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
never reported, nor, for an enum nested in a generic type, its enclosing type's arguments: `Palette<int>.Status` holds
no `int`.

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
(`RespectNullableAnnotations`), so it never skips `From`; one declared nullable (`InvoiceNumber?`) may be null. A
constructor member missing from the JSON fails to read too (`RespectRequiredConstructorParameters`). So that this covers
every value object an event carries, an architecture rule per strict event-sourced module requires each one to be a
constructor (positional) parameter of the event, never a property set by an initialiser (`{ get; init; }`). The rule
checks the constructor System.Text.Json actually selects (the one marked `[JsonConstructor]`, else the public
parameterless one, else the only public one), not every public constructor: beside a public parameterless constructor,
a constructor taking `InvoiceNumber` is never called when an event is read. It also refuses an optional parameter for a
value object that is not declared nullable (`InvoiceNumber Number = null!`), since an omitted optional parameter takes
its default instead of failing; `InvoiceNumber? Previous = null` stays allowed. The rule looks into the domain types an
event holds too, at any depth (alone, nullable, in a collection or an array), and checks their public properties the
same way: an event taking `InvoiceDetails Details` by constructor still reads `"Details":{}`, so `InvoiceDetails` takes
its `InvoiceNumber` by constructor as well, and a property holding a domain type that carries a value object (an
`IReadOnlyList<InvoiceLine>` of lines built from value objects) is itself a constructor parameter. A domain type is a
concrete type of the module's Domain that is not a value object, an enum or a collection. The walk follows only what
System.Text.Json reads: public properties that are neither indexers nor `[JsonIgnore]`d, and of an array or a collection
(a domain one too, `InvoiceNumberCollection : Collection<InvoiceNumber>`) its elements, not its members. It stops at a
value object, which is stored as its backing value, so `TypedId<Invoice>` never makes it look into `Invoice`, and it
reads a generic domain type through its members only, so `Reference<TTag>(InvoiceId Id)` used as `Reference<Invoice>`
does not either; the arguments of a generic type from outside the Domain (`Nullable<T>`, `Option<T>`) are walked. It
walks each closed type once, so a type that holds itself (`InvoiceChain? Next`) or a collection of itself
(`RecursiveCollection : Collection<RecursiveCollection>`) ends it, and `Envelope<Marker>` and `Envelope<InvoiceNumber>`
are checked each. Before it starts, it decides, as the primitive walk does (below), which generic definitions of the
Domain can recur forever: one that passes a type parameter on, inside a larger type, to a type that leads back to it
(`Node<T>` holding a `Node<Envelope<T>>`), through members or through a collection's elements, and each definition it
builds from its own type parameters. Every other definition has finitely many closed types, and each is checked, so a
larger closed type that does not grow (`Box<T>` holding the fixed `Box<List<InvoiceNumber>>`) is checked like any
other. Whether a closed type's member carries a value object depends only on which of the type arguments it reads
carry one, so a definition that recurs is checked once for each combination of those and of the violations it then
shows (an optional `T? Value = default` is nullable for a class but not for a struct), on the first closed type found
with it, and of every later closed type with the same combination only the arguments it reads are walked:
`Node<Marker>` and `Node<Envelope<Marker>>` are checked, and `Node<Envelope<Marker>>.Value` is named. There are
finitely many combinations, so the walk ends. One limit remains, for these definitions only: a member is named once,
after the first closed type found carrying a value object there, not once per closed type, since there are infinitely
many. A collection cannot recur through its own elements alone (`Growing<T> : Collection<Growing<List<T>>>`): the
runtime refuses to load such a type, so the module fails to load before any rule runs. Both settings hold
for every event and document in the store, read models included: a read model whose constructor later gains a
parameter can no longer read the rows stored before it, so give the new parameter a default or rebuild the projection.

**Known gap: a `null` element in a collection.** System.Text.Json applies neither nullable annotations nor converters to
a collection's elements, so `IReadOnlyList<InvoiceNumber>` reads `[null]` as a list holding a null, without `From`.
Stored events are written by the application, which never writes one; the gap is accepted, not enforced.

**Not yet in event-sourced modules: value objects over several values.** The converter writes a value object as its one
backing value, so `Money` (an amount and a currency) cannot be stored in an event. An architecture rule per strict
event-sourced module refuses a value object that is not an `IValueObject<T>` for exactly one `T`, until a JSON shape for
several values is decided; then the rule can be lifted.
{{~ end ~}}

## Enforced

An architecture rule per strict module walks from its aggregates, commands and domain events, through public
properties and public fields, and fails on each primitive it reaches, naming the type and member. A second rule per
strict module fails on each public setter of an aggregate root: strict is light and more.

The walk visits each closed type once. Before it starts, it decides from the module's generic definitions which ones can
recur forever: a definition that passes one of its type parameters on, inside a larger type, to a type that leads back
to it (`Node<T>` holding a `Node<List<T>>`) would construct a new closed type at every step. Every other definition has
finitely many closed types to visit, and the walk visits each of them, so a larger closed type that does not grow
(`Box<T>` holding the fixed `Box<List<string>>` and a `Cell<T>`) is walked like any other, and `Cell<List<string>>` is
reached.

A definition that can recur forever, and each definition it builds from its own type parameters, has its state checked
once, with each type parameter standing for everything an argument for it was found to hold, wherever the walk uses the
definition: `Sleeve<T>` holding a `Holder<T>` and a `Sleeve<Tuple<T, int>>`, used as `Sleeve<Marker>`, finds that its
`T` can hold an `int`, so `Holder<Tuple<Marker, int>>` is named for it. These sets only grow and draw on finitely many
types, so the walk ends, and it names every member and primitive a walk of all the closed types would name. What a
value object holds as its backing value stays exempt there too, and a phantom tag is never looked into: `Tag<TTag>`
holds a `Guid`, not its tag, so `Tag<Customer>` never makes the walk look into `Customer`.

Two limits remain, both for these definitions only:

- **One name per finding.** The walk names a member holding a primitive once, after the first closed type it found
  holding it there, not once per closed type: there are infinitely many.
- **A subclass may be walked that is never reached.** The walk enters each subclass the module declares of a closed type
  it reaches (`SpecialNode : Node<List<Waypoint>>`), or of an interface it reaches through the interface's variance
  (`SpecialNode : INode<List<Marker>>` is an `INode<IEnumerable<Marker>>` when `T` is `out`). For such a definition it
  tells whether a closed type is reached by matching each type argument on its own, so a subclass of arguments the
  module never uses together is walked too: with `Pair<Cargo, Marker>` and `Pair<Marker, Cargo>` reached, a subclass of
  `Pair<Cargo, Cargo>`. Under an `in` type parameter, an argument the definition builds from its own type parameters
  is taken to match any type. Either way the walk can name a primitive no aggregate reaches; it never hides one.

The exemptions hold there as they do for closed types: an enum is allowed, and a computed property `T Value` is an
answer in `Answer<bool>` but state in `Answer<List<bool>>`; a field that hides it stays state.

## Consequences

- More types, and an endpoint converts a request's primitives into value objects before it builds a command.
- A value read from the database passes the same guards as one a caller sends.

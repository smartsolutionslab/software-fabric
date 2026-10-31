# Absence: Option<T>

**Status:** Accepted (generated)

## Decision

When a value may be absent — a lookup that finds nothing, an optional domain attribute passed around — Domain and Application code says so with `Option<T>` (`src/Kernel`): `Option<T>.Some(value)`, `Option<T>.None`, `HasValue`, `Value`, `Match`, `Map`, `GetOrDefault`. A nullable reference says only that a value *may* be null; an option says what absence means and makes the caller handle it.

Enforced for return values: no public method in a module's Domain or Application project returns a nullable reference. Persisted state keeps nullable properties (EF Core maps those), and Infrastructure and Api keep nullable parameters where they translate wire and framework shapes.

This is the convention of Smart Sentinel Eye.

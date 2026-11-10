# Absence: Option<T>

**Status:** Accepted (generated)

## Decision

When a value may be absent — a lookup that finds nothing, an optional domain attribute passed around — Domain and Application code says so with `Option<T>` (`src/Kernel`): `Option<T>.Some(value)`, `Option<T>.None`, `HasValue`, `Value`, `Match`, `Map`, `GetOrDefault`. A nullable reference says only that a value *may* be null; an option says what absence means and makes the caller handle it.

`Option<T>` is a C# 15 union of its two cases, `Some<T>` and `None`: a `switch` on an option matches them (`Some<Invoice> some => some.Value`, `None => ...`), and one that misses a case does not compile, where a forgotten `HasValue` check only fails when it runs. The default option is `None`. The union's members live on its nested `IUnionMembers` interface, so `HasValue` and `Value` stay the option's own, and a case is matched without boxing.

Enforced for return values: no public method in a module's Domain or Application project returns a nullable reference. Persisted state keeps nullable properties (EF Core maps those), and Infrastructure and Api keep nullable parameters where they translate wire and framework shapes.

This is the convention of Smart Sentinel Eye.

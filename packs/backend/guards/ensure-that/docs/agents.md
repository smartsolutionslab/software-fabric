## Argument guards: Ensure.That

- Guard arguments with `Ensure.That(x)`: `.IsNotNull()`, `.IsNotNullOrWhiteSpace()`, `.HasMaxLength(n)`, `.Matches(regex, message)`, `.IsNotEmpty()` for `Guid`, `.AtLeast(n)` / `.InRange(min, max)` for numbers, `.Satisfies(predicate, message)` for the rest.
- Every failed guard throws `GuardException` (an `ArgumentException`); do not throw `ArgumentException` by hand for a precondition, add a method to the chain or use `.Satisfies(...)`.
- `ArgumentNullException.ThrowIfNull` and the other BCL `ThrowIf*` helpers do not compile here (RS0030).
- Validate at trust boundaries and in value-object factories; do not guard every private method.

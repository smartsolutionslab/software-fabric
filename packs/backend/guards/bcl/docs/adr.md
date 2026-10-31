# Argument guards: the BCL helpers

**Status:** Accepted (generated)

## Decision

Argument preconditions use the BCL: `ArgumentNullException.ThrowIfNull(x)`, `ArgumentException.ThrowIfNullOrWhiteSpace(name)`, `ArgumentOutOfRangeException.ThrowIfNegative(amount)`. No guard library.

## Consequences

- Familiar to every .NET developer; longer chains (length, pattern) are written as `if (...) throw new ArgumentException(...)`.

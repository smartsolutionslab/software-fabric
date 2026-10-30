## Argument guards: BCL

- Guard arguments with `ArgumentNullException.ThrowIfNull(x)`, `ArgumentException.ThrowIfNullOrWhiteSpace(x)` and the `ArgumentOutOfRangeException.ThrowIf*` helpers, at trust boundaries and in value-object factories.

## Absence: Option<T>

- A Domain or Application method that may find nothing returns `Option<T>`, never `T?`. An architecture rule checks return values.
- Handle it with `Match(some, none)`, `Map`, or `HasValue`/`Value`; never `.Value` without checking.
- Persisted properties stay nullable where a column is nullable; EF Core does not map `Option<T>`.

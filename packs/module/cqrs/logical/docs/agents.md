## CQRS: logical

- A command (`Commands/<UseCase>/`) changes state through an aggregate and returns at most an identifier or nothing.
- A query (`Queries/<UseCase>/`) reads with `AsNoTracking()` and projects to a DTO; it never changes state.
- Commands and queries are never mixed in one handler.

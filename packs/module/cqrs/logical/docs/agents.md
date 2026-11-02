## CQRS: logical

- A command (`Commands/<UseCase>/`) changes state through an aggregate and returns at most an identifier or nothing.
- A query (`Queries/<UseCase>/`) reads with `AsNoTracking()` and projects to a DTO; it never changes state.
- Commands and queries are never mixed in one handler.
- A model becomes a DTO in one place: a `<Model>Mappings` static class beside the DTO, with a `To<Name>Dto()` extension method (`ToDto()` when it is the model's only DTO). Inside an EF `Select` use its `To<Name>DtoExpression()`, an `Expression<Func<Model, Dto>>`, as `.Select(InvoiceMappings.ToInvoiceDtoExpression())`: EF cannot translate a method call.
- Handlers never build a `*Dto` themselves: not with `new`, not in a lambda, not in a private `ToDto`/`Map` helper. An architecture rule fails the build. Mappings are pure: no I/O, no `Result`, no logging.

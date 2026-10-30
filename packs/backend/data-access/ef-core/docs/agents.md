## Data access: EF Core

- A module's persistence lives in its Infrastructure project: one `<Module>DbContext : ModuleDbContext`, and one `IEntityTypeConfiguration<T>` per entity under `Persistence/Configurations/`.
- Never reference another module's DbContext or tables. Each module's tables live in its own schema.
- The connection string is named `database` and comes from Aspire; never hard-code one.
- A model change needs a migration in the same PR: `dotnet ef migrations add <Name> --project src/Modules/<Module>/<Module>.Infrastructure --startup-project src/MigrationRunner --context <Module>DbContext`. Never edit an applied migration; add another.
- Migrations run in `src/MigrationRunner` before the host starts; the host never migrates. A model without a matching migration makes the runner fail.

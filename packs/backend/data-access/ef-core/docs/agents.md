## Data access: EF Core

- A module's persistence lives in its Infrastructure project: one `<Module>DbContext : ModuleDbContext`, and one `IEntityTypeConfiguration<T>` per entity under `Persistence/Configurations/`.
- Never reference another module's DbContext or tables. Each module's tables live in its own schema.
- The connection string is named `database` and comes from Aspire; never hard-code one.

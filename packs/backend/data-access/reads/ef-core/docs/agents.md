## Reads: EF Core

- Query handlers read through EF Core, like everything else in a module. Do not add Dapper (or another SQL mapper) to a module; an architecture rule fails the build if one appears.
- A query that EF Core cannot express well is a project decision (`backend.data-access.reads: dapper`, chosen with its own ADR): escalate it with `needs:decision` instead of starting it in one module.

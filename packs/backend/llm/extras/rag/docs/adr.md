# Retrieval: pgvector

**Status:** Accepted (generated)

## Decision

A module can index its own records as vectors and retrieve the ones nearest to a question, to hand them to the model (ADR-0012 of the generator).

- **Vectors live in PostgreSQL with pgvector**, through the AppHost's database: the PostgreSQL resource runs the image `pgvector/pgvector:0.8.7-pg18` (pgvector 0.8.7 on PostgreSQL 18). Each module keeps one table, `embeddings (key text primary key, content text not null, embedding vector(N) not null)`, in its own schema, with an HNSW index for cosine distance (`vector_cosine_ops`, `<=>`). N is the embeddings model's size, `VectorTables.Dimensions`.
{{~ if project["backend.tenancy"] == "column" ~}}
- **Each tenant's vectors are its own** (tenancy: a tenant column). The table is `embeddings (tenant_id text not null, key text not null, content text not null, embedding vector(N) not null, primary key (tenant_id, key))`: a key is unique within a tenant, and the primary key's index, led by `tenant_id`, also finds one tenant's records. The index takes the tenant from `ITenantContext`, the source the module's DbContext filters by, never from its caller; it is scoped, as that source is. `IndexAsync` stores under the current tenant and fails without one; `RemoveAsync` removes only the current tenant's record; `SearchAsync` returns only the current tenant's records, and nothing without a tenant. The HNSW index covers every tenant's vectors and the tenant filter is applied during its scan. That scan stops after `hnsw.max_scan_tuples` (20,000 by default) even when iterative, so where other tenants' records lie nearer it can come back short. A search that returns fewer records than asked therefore ranks the tenant's records exactly instead, read through the primary key's index.
{{~ end ~}}
- **The MigrationRunner creates them**, after the modules' EF Core migrations: the `vector` extension once, then each module's schema, table and index, each only if it is missing, so running it again changes nothing. The host never creates or alters them, and the tables are not part of any EF Core model.
- **`ModuleVectorIndex<TContext>`** (`src/BuildingBlocks/Rag`) is a module's index, over Npgsql with the `Pgvector` package: `IndexAsync(key, text)` embeds the text and stores it under the key, replacing what was there; `RemoveAsync(key)` removes it; `SearchAsync(query, count)` returns the nearest records first as `RetrievedPassage(Key, Text, Distance)`, widening the HNSW scan to the count (`hnsw.ef_search`, 40 to 1000) and letting it go on in order beyond that (`hnsw.iterative_scan = strict_order`), so a wide search does not come back short. Its table is the one in the schema of `TContext`, the module's own DbContext.
- **A module reads only its own vectors.** No module names another module's DbContext in a vector index; another module's records are reached through `src/Contracts`.
- **Retrieved text is untrusted input to the model.** A record a user wrote can carry instructions; once retrieved it reaches the model as data, in a delimited user message, never in system instructions, and is never executed, interpolated into SQL or markup, or used as a tool argument without checking.
- **Tests never call a real model**: `FakeEmbeddingGenerator` (`tests/Fakes`) embeds by word hashes, deterministically.

## Consequences

- An architecture rule, `ModulesSearchOnlyTheirOwnVectors`, fails the build when a module's type uses `ModuleVectorIndex<T>` with another module's DbContext or over a generic parameter, or passes another module's DbContext to a generic type or method, in a signature, a field, a local or a method body, naming the type and both modules.
- `tests/Rag.Tests` starts the AppHost's database alone, runs the MigrationRunner against it, and checks every module's table and index, that a search returns the nearest records first, also more of them than HNSW's default breadth of 40, and that indexing a key again replaces it; no host, no model, no network.
- What the gates cannot see: whether the text a module indexes is fit to be sent to the embeddings provider, and whether a prompt keeps retrieved text apart from instructions. Every indexed record and every query is a request to the provider: it costs money and sends its content there.
- Changing the embeddings model to one of another size means tables of that size, made by a migration step of the project's own, and every record indexed again.
{{~ if project["backend.tenancy"] == "column" ~}}
- Work outside a request indexes and searches only inside a `TenantScope`; without one it finds nothing and cannot index.
- One HNSW index serves every tenant. A search that the index answers short, because a tenant has fewer records than asked or other tenants' records crowd its own out, costs an exact ranking of that tenant's records. A project whose tenants grow large and very unequal may want partial indexes or partitions per tenant; that is a decision of its own.
- This pack keeps vectors apart by a tenant column only: with `schema-per-tenant` or `database-per-tenant` tenancy the build fails rather than sharing vectors between tenants.
{{~ else ~}}
- The vector table has no tenant column, as the project has no tenancy (`backend.tenancy: none`); with a tenant column (`column`) each record belongs to a tenant, and with any other tenancy the build fails rather than sharing vectors between tenants.
{{~ end ~}}

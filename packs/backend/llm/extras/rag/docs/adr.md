# Retrieval: pgvector

**Status:** Accepted (generated)

## Decision

A module can index its own records as vectors and retrieve the ones nearest to a question, to hand them to the model (ADR-0012 of the generator).

- **Vectors live in PostgreSQL with pgvector**, through the AppHost's database: the PostgreSQL resource runs the image `pgvector/pgvector:0.8.7-pg18` (pgvector 0.8.7 on PostgreSQL 18). Each module keeps one table, `embeddings (key text primary key, content text not null, embedding vector(N) not null)`, in its own schema, with an HNSW index for cosine distance (`vector_cosine_ops`, `<=>`). N is the embeddings model's size, `VectorTables.Dimensions`.
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
- The vector table has no tenant column, so this pack supports `backend.tenancy: none` only so far; with a tenancy choice the build fails rather than sharing vectors between tenants.

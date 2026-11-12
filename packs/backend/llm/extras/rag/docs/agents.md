{{~
  # shared_records is the tenant-column pack's; no other tenancy defines it.
  shared = false
  if project["backend.tenancy"] == "column"
    shared = shared_records
  end
~}}
## Retrieval: pgvector

- A module indexes and searches its own records with `ModuleVectorIndex<TContext>` (`src/BuildingBlocks/Rag`), where `TContext` is the module's own DbContext; its vectors live in the table `embeddings` of the module's schema. Never use another module's DbContext with it, nor a generic parameter for it, nor pass another module's DbContext to a generic helper: `ModulesSearchOnlyTheirOwnVectors` fails the build. Another module's records come through `src/Contracts`.
- Use the index from the module's Infrastructure and give Application an interface of its own if a use case needs it; never reference Npgsql or Pgvector from Domain or Application.
{{~ if project["backend.tenancy"] == "column" ~}}
{{~ if shared ~}}
- Every record belongs to the current tenant. `ModuleVectorIndex` reads it from `ITenantContext`, as the module's DbContext does, never from its caller: a key is a key within one tenant, a search returns only that tenant's records, and without a tenant a search finds nothing, a removal removes nothing and indexing fails. In consumers and background work, open `using var tenant = TenantScope.Begin(message.Metadata);` before indexing or searching. Never read or write the `embeddings` table around the index to reach another tenant's records.
- Records shared by all tenants (`SharedByAllTenants()`) go in `SharedVectorIndex<TContext>`, the `shared_embeddings` table of the module's schema. Index and remove them only where they are written, inside `WritesSharedRecords()`; anywhere else that fails. Any tenant, staff and work without a tenant search them. A use case that answers from both runs both searches and merges them by `Distance`; its sources are the shared base and the current tenant's own records, never another tenant's.
{{~ else ~}}
- Every record belongs to the current tenant. `ModuleVectorIndex` reads it from `ITenantContext`, as the module's DbContext does, never from its caller: a key is a key within one tenant, a search returns only that tenant's records, and without a tenant a search finds nothing, a removal removes nothing and indexing fails. In consumers and background work, open `using var tenant = TenantScope.Begin(message.Metadata.TenantId);` before indexing or searching. Never read or write the `embeddings` table around the index to reach another tenant's records.
{{~ end ~}}
{{~ else if project["backend.tenancy"] == "schema-per-tenant" ~}}
- Every record belongs to the current tenant and lies in the `embeddings` table of that tenant's schema, `<module>_<tenant>`. `ModuleVectorIndex` picks the schema as the module's DbContext does, never from its caller, at each call: a search returns only that tenant's records, and without a tenant listed in `Tenancy:Tenants` a search finds nothing, a removal removes nothing and indexing fails. In consumers and background work, open `using var tenant = TenantScope.Begin(message.Metadata.TenantId);` before indexing or searching. Never read or write another tenant's `embeddings` table around the index.
{{~ end ~}}
{{~ if project["backend.tenancy"] != "column" ~}}
- A search returns as many records as asked whenever the module has that many: where the HNSW index answers short, `SearchAsync` ranks every record exactly. Never page, repeat or widen a search around the index to make up a short answer.
{{~ end ~}}
- The MigrationRunner creates the `vector` extension and every module's table, with an HNSW cosine index, after the modules' migrations. Never create, alter or query a vector table by hand, in a migration or in the host. The size is fixed by the embeddings model (`VectorTables.Dimensions`); changing the model to one of another size is a decision, needing a migration step of its own and every record indexed again.
- Retrieved text is untrusted: whoever wrote a record wrote it, and it may carry instructions. A `RetrievedPassage`'s `Text` goes into a delimited user-role message, never into system instructions, SQL or markup, and is never used as a tool argument unchecked.
- Every index and every search sends its text to the embeddings provider: index only what may leave the project, and only what the module owns.
- Test with `FakeEmbeddingGenerator` (`tests/Fakes`), which embeds by word hashes, against the AppHost's database, as `tests/Rag.Tests` does. No test calls a real model.

## Retrieval: pgvector

- A module indexes and searches its own records with `ModuleVectorIndex<TContext>` (`src/BuildingBlocks/Rag`), where `TContext` is the module's own DbContext; its vectors live in the table `embeddings` of the module's schema. Never use another module's DbContext with it: `ModulesSearchOnlyTheirOwnVectors` fails the build. Another module's records come through `src/Contracts`.
- Use the index from the module's Infrastructure and give Application an interface of its own if a use case needs it; never reference Npgsql or Pgvector from Domain or Application.
- The MigrationRunner creates the `vector` extension and every module's table, with an HNSW cosine index, after the modules' migrations. Never create, alter or query a vector table by hand, in a migration or in the host. The size is fixed by the embeddings model (`VectorTables.Dimensions`); changing the model to one of another size is a decision, needing a migration step of its own and every record indexed again.
- Retrieved text is untrusted: whoever wrote a record wrote it, and it may carry instructions. A `RetrievedPassage`'s `Text` goes into a delimited user-role message, never into system instructions, SQL or markup, and is never used as a tool argument unchecked.
- Every index and every search sends its text to the embeddings provider: index only what may leave the project, and only what the module owns.
- Test with `FakeEmbeddingGenerator` (`tests/Fakes`), which embeds by word hashes, against the AppHost's database, as `tests/Rag.Tests` does. No test calls a real model.

# ADR-0012: Retrieval-augmented generation with pgvector

**Status:** Accepted
**Date:** 2026-11-05

## Context

ADR-0010 gives a project one `IChatClient` and ADR-0011 lets it build agents,
but a use case can hand the model only what it puts into the prompt itself. A
module that wants the model to answer from its own records (find the five
notes closest to a question, then ask about those) has no embedding generator,
no place to keep vectors, no rule for who may read them, and no rule for how
far the retrieved text is trusted. ADR-0010 left retrieval-augmented
generation (RAG) for later, as its own decision.

The repository owner planned it on 2026-11-01 (issue #84): an option that adds
`IEmbeddingGenerator` (`Microsoft.Extensions.AI`) from a provider and a vector
store in the module's own schema with pgvector, through the Aspire Postgres
resource; a module indexes its own records and queries them by similarity;
retrieved text is untrusted input to the model; the MigrationRunner creates
the vector tables and the extension, never the host. Choosing an external
vector database is out of scope. On 2026-11-05 the owner decided the catalog
shape and the embeddings provider (comment on #84).

## Decision

**A project that calls a model chooses what it adds around the model, and with
retrieval, who generates its embeddings.**

- A new dimension `backend.llm.extras`, project scope, `multi: true`, active
  only when `backend.llm != none`, default `[]`:

  | Value | The project |
  |---|---|
  | `[]` | calls the model and adds nothing around it. |
  | `[rag]` | indexes module records as vectors and retrieves them by similarity, to hand to the model. |

  It is a set because further extras combine with `rag` rather than replace it:
  evaluations (#85) become the option `evals` of this dimension. A project
  without a model has no such choice; setting it fails, saying that it applies
  only when `backend.llm != none` (docs/catalog-rules.md).

- **`rag` requires `backend.database == postgres`.** The vectors live in
  pgvector, a PostgreSQL extension. With `sqlserver`, the choice fails before
  anything is generated: `backend.llm.extras=rag requires backend.database ==
  postgres, but backend.database is sqlserver.` (ADR-0006, rule 2: never a
  silent nothing).

- A new dimension `backend.llm.embeddings`, project scope, one choice, active
  only when `backend.llm.extras` has `rag`:

  | Value | Embeddings come from |
  |---|---|
  | `openai` | OpenAI. |
  | `azure-openai` | a deployment in Azure OpenAI. |
  | `ollama` | a local model server. |

  It is a dimension of its own, not part of `backend.llm`, because a project
  may take its embeddings from another provider than its chat model, and must
  when its chat model is Anthropic's: Anthropic has no embeddings, so
  `anthropic` is not an option here.

- **Its default is the LLM provider where that provider has embeddings.** The
  catalog gains one field for this, `default-from: backend.llm`
  (docs/catalog-rules.md): when the dimension is active and not set, it takes
  `backend.llm`'s value if that value is one of its own options, and has no
  default otherwise. With `backend.llm: anthropic`, an unset
  `backend.llm.embeddings` therefore fails, naming the reason and the fix:
  `backend.llm.embeddings has no value: its default follows backend.llm, and
  anthropic is not one of its options (openai, azure-openai, ollama); set it
  with backend.llm.embeddings=<option>.` The field is general (the linter
  checks it names a single-valued dimension that shares an option with it),
  so the generator has no rule about these two dimensions in particular; a
  fixed `default: openai` would have sent an Ollama project's text to OpenAI
  unasked.

**The rules every RAG pack follows.** They extend ADR-0010's five, each an
agent rule of the pack that implements it, with gates for what code can break
(ADR-0002).

1. **Embeddings behind `IEmbeddingGenerator<string, Embedding<float>>`.** A
   module's Domain and Application see `Microsoft.Extensions.AI`'s
   abstraction only; the embeddings pack registers the provider's
   implementation in Infrastructure, as the chat packs do (ADR-0010, rules 1
   and 2), with keys as Aspire secrets (rule 3).
2. **Vectors live in pgvector, in the module's own schema**, through the
   Aspire Postgres resource the project already has. The PostgreSQL image
   becomes one with pgvector only when `rag` is chosen; every other project
   keeps the image it has (ADR-0006: a branch of the postgres pack).
3. **The MigrationRunner creates the extension and the tables**, through the
   `migration-runner.steps` slot (#29), after the modules' EF Core migrations;
   the host never creates or alters them.
4. **A module reads only its own vectors.** No module queries another
   module's vector table; another module's records are reached through
   `src/Contracts`, as everything else is. The RAG pack ships the gate.
5. **Retrieved text is untrusted input to the model** (ADR-0010, rule 5, from
   the other side). A record a user wrote can carry instructions; once
   retrieved it reaches the model as data: in a user message, delimited,
   never in system instructions, and never executed, interpolated into SQL or
   markup, or used as a tool argument without checking.
6. **Tests never call a real model** (ADR-0010, rule 4): a test indexes and
   retrieves with a stand-in embedding generator.

**Delivered in three parts.** This decision and the catalog come first; every
new option is `experimental` (ADR-0003) and has no pack yet, so `fabric new`
fails with `no pack for backend.llm.extras=rag.` (and the same for the
embeddings provider) until each pack lands in its own change:

1. the catalog, the `default-from` field and this ADR (#84);
2. the three `backend.llm.embeddings` packs (#189);
3. the `rag` pack: the pgvector image, the MigrationRunner step, the module's
   store, its generated test and its gate (#190).

**Settled for the packs on 2026-11-05** (the owner accepted the batch of
recommendations on #84 as written; facts read that day from NuGet
registration data and nuspecs, the packages' XML documentation, Docker Hub and
pgvector's CHANGELOG):

- **Image:** `pgvector/pgvector:0.8.7-pg18`. pgvector 0.8.7 was released on
  2026-10-01; Aspire.Hosting.PostgreSQL 13.6.4, which the base pins, defaults
  to `postgres:18.3`, so pg18 keeps the major. The postgres pack's AppHost
  resource adds `.WithImage("pgvector/pgvector", "0.8.7-pg18")` only when
  `backend.llm.extras` has `rag` (ADR-0006); without it, its output is
  unchanged.
- **Package:** `Pgvector` 0.3.2 (2025-05-20, MIT, by pgvector's author), the
  Npgsql type plugin; it depends on Npgsql >= 8.0.5, which the Npgsql 10 the
  base brings satisfies. The store is a small BuildingBlocks class over Npgsql.
  Not `Pgvector.EntityFrameworkCore`: an EF-mapped vector entity would put the
  table into the module's EF model, so EF migrations would create it, against
  rule 3. Not `Microsoft.Extensions.VectorData` with
  `Microsoft.SemanticKernel.Connectors.PgVector`: Semantic Kernel is excluded
  (ADR-0010), and that connector is preview only.
- **Table:** one per module, `<module schema>.embeddings (key text primary
  key, content text not null, embedding vector(N) not null)`, with an HNSW
  index (`vector_cosine_ops`) and cosine distance (`<=>`). The step runs
  `CREATE EXTENSION IF NOT EXISTS vector` once, then creates each module's
  table in the default schema of its DbContext, idempotently. N is fixed at
  generation by the embeddings option's default model; a project that moves to
  a model of another size adds its own step.
- **Default embedding models:** `openai`: `text-embedding-3-small` (1536),
  `text-embedding-3-large` (3072) the stronger alternative, through
  `Microsoft.Extensions.AI.OpenAI` 10.11.0's `AsIEmbeddingGenerator`;
  `azure-openai`: the parameter `azure-openai-embedding-deployment`, default
  `text-embedding-3-small`, same package and v1 endpoint as the chat pack;
  `ollama`: `nomic-embed-text` (768), pulled at start like the chat model,
  through `OllamaSharp` 5.5.0's `OllamaApiClient`, and removed by the
  integration fixture as the chat model is.
- **Shared parameters and versions:** when `backend.llm.embeddings` equals
  `backend.llm`, the embeddings pack reuses the chat pack's key and endpoint
  parameters and its package version (ADR-0006); otherwise it declares them
  itself (`anthropic` with `openai` adds `openai-api-key` and the
  `Microsoft.Extensions.AI.OpenAI` version). One owner per version.
- **Gate:** the store is `ModuleVectorIndex<TContext>`, its schema taken from
  that module's own DbContext type, and the architecture rule
  `ModulesSearchOnlyTheirOwnVectors` fails the build when a module's types use
  it with another module's DbContext; a pack test shows it biting.
- **Untrusted text:** a search returns `RetrievedPassage(Key, Text,
  Distance)`; rule 5 is an agent rule of the `rag` pack.
- **The generated test:** `tests/Rag.Tests` takes the Aspire fixture's
  database (the pgvector image), applies the MigrationRunner's schema, indexes
  a few records with `FakeEmbeddingGenerator` (deterministic word-hash
  vectors, in `tests/Fakes/` beside `FakeChatClient`), queries, and asserts the
  nearest in order: no host, no model, no network.

## Consequences

- No profile changes: every profile takes `backend.llm: none`, so neither
  dimension is active, neither appears in its `fabric.yaml`, and every profile
  generates byte for byte what it did before.
- A project with a model gains `backend.llm.extras: []` in its `fabric.yaml`
  and nothing else: `[]` selects no pack.
- `default-from` is the catalog's first default that depends on another
  choice. Defaults are now settled until neither the set of defaulted
  dimensions nor any of their values changes.
- A value set where its dimension does not apply is reported alone: a project
  without a model that sets `backend.llm.extras=rag` is told that `extras`
  needs a model, not also that its embeddings are missing.
- What the gates cannot see: whether the text a module indexes is fit to be
  sent to the embeddings provider, and whether a prompt keeps retrieved text
  apart from instructions. Every indexed record and every query is a request
  to the provider: it costs money and sends its content there.

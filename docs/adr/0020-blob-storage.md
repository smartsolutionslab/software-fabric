# ADR-0020: Blob storage, `backend.blob-storage` (`none`, `azure-blob`)

**Status:** Accepted (owner, 2026-11-22, at the review of PR #870 for #864, M5-04). It records the owner's decisions
B1 to B3 and X1 (2026-11-21 and 2026-11-22, on #602), and O1 to O5, which the owner decided at the review by accepting
each recommendation as written.
**Date:** 2026-11-22

## Context

ADR-0001 and the M4 decisions E3 and E7 defer a catalog dimension until a pilot has built it by hand, so that its
pack is designed from working code. M5's plan (`docs/roadmap.md`, M5 tasks, "File storage") makes blob storage one
of them. The proposal on #602 (section 2, "Document / blob storage") described fabric-pilot's attachments and the
shape a pack would take; the owner accepted it on 2026-11-21 with one change, B1: write the ADR now, from two pilots,
instead of waiting for fabric-pilot's documents module.

The two pilots cover both tenancy branches a generated project can have today, `none` and `column`. What follows is
read from fabric-pilot at `c137199` (origin/main) and from smartsolutionslab/pharma-training#3, merged as PR #124
(merge commit `8db1854`).

### fabric-pilot: ticket attachments, tenancy `column`

fabric-pilot (`profile: saas-ai-react`, `backend.tenancy: column`, modular monolith) stores the files of a ticket
(feature 2, fabric-pilot#6). The files pass through the host: the browser posts them with the ticket, and downloads
them from an endpoint.

- **The interface**, `src/Modules/Tickets/Tickets.Application/IBlobStore.cs`: `UploadAsync(name, content)` and
  `OpenReadAsync(name)`, which returns `Option<Stream>`. Its comment calls it "the one place a blob storage pack would
  replace".
- **The store**, `src/Modules/Tickets/Tickets.Infrastructure/Storage/AzureBlobStore.cs`: one container,
  `ticket-attachments`, created on first upload (`CreateIfNotExistsAsync`, no public access). An upload overwrites
  (`overwrite: true`). A missing blob or container is `None`.
- **The connection**, `src/Modules/Tickets/Tickets.Infrastructure/Storage/BlobServiceClients.cs`: Aspire's `blobs`
  connection is Azurite's connection string locally and, deployed, the account's blob endpoint, reached with
  `DefaultAzureCredential` rather than a key. `Wiring/TicketsStorage.cs` registers it as a singleton.
- **The names**, `src/Modules/Tickets/Tickets.Application/AttachmentBlobs.cs`: `{ticket}/{attachment}`, two Guid v7
  values. `Commands/RaiseTicket/RaiseTicketHandler.cs` uploads before the ticket is saved, so a failed save leaves
  blobs nothing names.
- **The AppHost**, `src/AppHost/AppHost.cs`: `AddAzureStorage("storage").RunAsEmulator(azurite =>
  azurite.WithDataVolume()).AddBlobs("blobs")`, referenced by the host with `.WithReference(blobs).WaitFor(blobs)`.
- **The deployment**, `infra/resources.bicep` (`storage`, `blobsForHost`) and `infra/README.md`: a `StorageV2`
  account with `allowSharedKeyAccess: false` and `allowBlobPublicAccess: false`, Storage Blob Data Contributor for the
  apps' managed identity, and since fabric-pilot#87 a private endpoint only (`infra/private-endpoint.bicep`,
  `docs/adr/0055-deployment-private-network.md`).
- **The tests**: `tests/Modules/Tickets.Tests/InMemoryBlobStore.cs`, `Storage/BlobServiceClientsTests.cs`,
  `Queries/DownloadAttachmentTests.cs`; and against the AppHost `tests/Integration.Tests/TicketAttachmentsTests.cs`
  (files come back unchanged; a file over the limit is refused; `Another_tenants_ticket_and_its_attachments_are_not_found`)
  and `BlobStorageTests.cs` (Azurite keeps its files in a volume, as the database keeps its rows).

**The gap B2.** The blob names carry no tenant. `Queries/DownloadAttachment/DownloadAttachmentHandler.cs` reads the
ticket through the tenant's rows first, so another tenant's attachment is not found; but that rests on the order of
two lookups, and `IBlobStore` itself opens any name it is given. The owner decided (B2) that this is fixed in a
fabric-pilot issue, so that isolation no longer rests on lookup order. That issue is not open yet (fabric-pilot's
issues end at #88 as this is written).

fabric-pilot has no ADR of its own for its blob storage; X2 (2026-11-21) adds one in a fabric-pilot issue.

### PharmaTraining: one container per module, tenancy `none`

PharmaTraining (`profile: saas-services-vue`, `backend.tenancy: none`, services topology with seven services) built
blob storage in smartsolutionslab/pharma-training#3, decision (c) there, recorded in its
`docs/adr/0002-blob-storage-one-container-per-module.md`. No file passes through a service or the edge: the browser
uploads and reads with short-lived signed URLs.

- **A container per module**, `src/AppHost/AppHost.cs`: one account, `storage`, on Azurite with a data volume; in the
  loop over the services `storage.AddBlobContainer($"{module}-blobs", module)` and `.WithReference(blobs, "blobs")`, so
  each service reads its own container, and only its own, as the connection string `blobs`.
  `tests/Integration.Tests/AppHostBlobContainersTests.cs` holds the model to it.
- **The interface**, `src/BuildingBlocks/Storage/IBlobUrls.cs`: `CreateUploadUrlAsync(BlobName, BlobUrlLifetime)` and
  `CreateReadUrlAsync(...)`, with no container parameter, so a service cannot ask for another module's container.
  `BlobName.cs` refuses a leading `/`, an empty, `.` or `..` segment, a URL, a backslash and control characters, up to
  1024 characters; `BlobUrlLifetime.cs` is more than zero and at most 15 minutes.
- **The signing**, `src/BuildingBlocks/Storage/AzureBlobUrls.cs`: a service SAS on one blob (`sr=b`), `sp=c` (create
  only, so a second PUT is refused) to upload and `sp=r` to read, expiring at most 15 minutes after signing with no
  start time, signed with the account key. A missing connection string, container name or key fails when the first
  URL is signed, naming it.
- **Registration**, `src/BuildingBlocks/Storage/Wiring/BlobStorageRegistration.cs` (`AddModuleBlobStorage`), called
  for every module from `src/BuildingBlocks/Modules/ModuleInfrastructure.cs`.
- **The browser's origin**, `src/AppHost/Wiring/BrowserUploads.cs` (`AllowBrowserAccessFrom`): in run mode, one CORS
  rule on the blob service for the web app's origin, set once Azurite is ready, since Aspire's storage integration has
  no CORS setting.
- **The gate** `tests/Architecture.Tests/Rules/BlobStorageSdkStaysInBuildingBlocks.cs`: no module references the
  Azure Storage SDK.
- **The tests**: `tests/BuildingBlocks/Storage.Tests/` (names, lifetimes, signing, registration) and
  `tests/Integration.Tests/BlobStorageTests.cs`, which sends each kind of URL to Azurite and checks what it may and
  may not do.

What PharmaTraining left open, by its own ADR 0002's Consequences:

- **Server-side copies are planned, not built.** "Under (c) a file another module needs is stored twice, once in
  each module's container", but how Learning reads the Editorial blob it copies is open for
  smartsolutionslab/pharma-training#41 (open), which weighs four ways: a read URL in the integration event, read access
  to the other container, an HTTP call, or the owner writing into the other container.
- **Signing when deployed.** Storage accounts Aspire provisions disable shared-key access, so there is no key to sign
  with; keyless signing (a user delegation key) is "a separate decision", and "until then this design is for local
  runs and tests". Aspire's publish-mode role assignments are account-wide, not per container.
- **The endpoints** that use `IBlobUrls` are pharma-training#15, #41 and #50, all open; no endpoint uses it yet.

### Where the two pilots agree and differ

| | fabric-pilot | PharmaTraining |
|---|---|---|
| Tenancy | `column` | `none` |
| Topology | modular monolith | services |
| Containers | one, `ticket-attachments`, named in code | one per module, `<module>-blobs` resource, container `<module>` |
| How files travel | through the host (upload, stream back) | browser to storage, signed URLs of at most 15 minutes |
| Overwrite | yes | no: create-only |
| Credential locally | Azurite's connection string | Azurite's account key |
| Credential deployed | managed identity, no key (`allowSharedKeyAccess: false`) | not decided: no key exists where Aspire provisions |
| Names | `{ticket}/{attachment}`, no tenant (B2) | `BlobName`, guarded, module-generated Guid v7 segments |
| SDK confined to | the module's Infrastructure | `src/BuildingBlocks/Storage` |
| Deployed | yes, by hand-written Bicep | not yet (pharma-training#114, Aspire's publisher) |

## Decision

### Settled by the owner

- **The dimension (B1, B3).** `backend.blob-storage`, project scope, default `none`:

  | Value | The project |
  |---|---|
  | `none` | stores no files outside its database. |
  | `azure-blob` | stores files in Azure Blob Storage: Azurite locally, a storage account when deployed. |

  There is no S3 or MinIO option in V1 (B3).

- **`changeable: true` (X1).** A pilot moves from its hand-written store to the pack with `fabric change --set
  backend.blob-storage=azure-blob`, the only way the pack is proven on the code it was designed from. ADR-0016 C2 marks
  only presentation dimensions changeable; M5-01's ADR (#861) amends C2 so that M5's dimensions may be, and this
  dimension relies on that amendment. C2's catalog lint still applies: no other dimension's `active-when`, `requires`
  or `default-from`, and no option's `requires` or `conflicts`, may name `backend.blob-storage`. The pack therefore
  branches on other choices with `when` (ADR-0006), and nothing else in the catalog depends on it.

- **The tenant is in the blob name, and the store puts it there (B2).** With tenancy `column` the store prefixes every
  name with the tenant it reads from `ITenantContext`, never from its caller, as `ModuleVectorIndex` does for
  `embeddings` (ADR-0012); without a tenant it opens nothing and writes nothing. Isolation must not rest on a lookup
  the module happens to run first. The integration test the pack ships, and M5-05's done-when names, reads another
  tenant's blob by its full name and is refused.

- **Both pilots are the reference (B1).** fabric-pilot for tenancy `column`, a modular monolith, files through the
  host, and a keyless deployed account; PharmaTraining for tenancy `none`, services, a container per module and
  signed URLs. Each pilot moves to the pack through `fabric change` once it lands (X1).

### The pack's shape, from #602 as accepted and the two pilots

- **A building block, `src/BuildingBlocks/Blobs`**, which alone names the Azure Storage SDK. A module takes its own
  module's store (#602: `ModuleBlobStore<TModule>`), which offers fabric-pilot's two operations (store a stream under
  a name, open one as `Option<Stream>`), both create-only (O5), with PharmaTraining's `BlobName` guarding the names it
  is given. PharmaTraining's signed URLs, guarded by its `BlobUrlLifetime`, follow once a pilot has signed one in
  Azure (O1). No operation takes a container, and none copies between containers (O2).
- **A container per module**, as a database schema is per module (#602; PharmaTraining's decision (c)):
  - modular monolith: the AppHost adds one account and its `blobs` connection, as fabric-pilot does; each module's
    store uses a container named after the module, created on first write, so no shared file gains a line per module
    (ADR-0005);
  - services: the AppHost adds the module's container in its loop over the services, as PharmaTraining does, and
    hands each service its own as `blobs` (ADR-0017).
- **Tenancy branches (ADR-0006).** `none`: names as given. `column` and `schema-per-tenant`: the tenant prefix
  above (B2, O3).
- **Slots**: `apphost.resources` (the account, Azurite with a data volume), `apphost.service-resources` (the
  container per service), `apphost.host-wiring` (`.WithReference`), `host.services` and the package versions; all
  exist today (`docs/slots.md`).
- **Gates** (#602, each seen failing first in M5-05):
  1. an architecture rule that the Azure Storage SDK is referenced only from `src/BuildingBlocks/Blobs` (O4), as
     PharmaTraining's `BlobStorageSdkStaysInBuildingBlocks` does;
  2. `ModulesUseOnlyTheirOwnContainer`, an architecture rule;
  3. an integration test that one tenant cannot read another tenant's blob by name (B2, with `column` and
     `schema-per-tenant`), and one that a second write to a name is refused (O5); when signed URLs follow (O1),
     PharmaTraining's `BlobStorageTests` with them: each URL does what it may and nothing else;
  4. the volume test, fabric-pilot's `BlobStorageTests`;
  5. with services, PharmaTraining's `AppHostBlobContainersTests`: every service reads its own container as `blobs`
     and no other.
- **AGENTS.md rules**: take the module's store, never a `BlobServiceClient`; names are generated by the module (a
  Guid v7 segment), never taken from user input such as a file name; a module that accepts an upload checks its size
  and content type before it records it, since a signed URL cannot limit either (PharmaTraining ADR 0002); another
  module's files come through `src/Contracts`, never its container; the key is never in a settings file.

### Decided at the owner's review (2026-11-22)

The pilots and B1 to B3 left five points open. The draft of this ADR gave each its options and a recommendation; the
owner accepted every recommendation as written. Each is kept with its reasoning.

- **O1. Signing when deployed.** fabric-pilot's deployed account has no key (`allowSharedKeyAccess: false`), and
  Aspire's publisher (D2) provisions accounts the same way, so PharmaTraining's account-key signing works only on
  Azurite. Neither pilot has signed a URL in Azure. Options: (a) the pack signs with a user delegation key under the
  app's identity, locally and deployed, so there is one code path, and M5-05 shows it on Azurite and in one deployed
  run; (b) the pack ships the stream operations only and leaves signed URLs until pharma-training#114 has deployed
  them; (c) account-key signing as PharmaTraining has it, deployed with shared keys enabled. **Decided: (b)**, with
  signed URLs added once a pilot has signed one in Azure (E3, E7: nothing the pilot has not run); (c) re-opens what
  both pilots' deployments rule out.
- **O2. Server-side copies between modules.** The owner's B1 describes PharmaTraining as using them; its ADR 0002
  records them as intended, with the mechanism open for pharma-training#41. **Decided:** the pack ships no
  cross-container copy and no operation that names another module's container; a module that needs another's file is
  told through an integration event and fetches or receives it as #41 decides, and the pack adds that once #41 is
  merged.
- **O3. Tenancy `schema-per-tenant`.** Neither pilot has it, but the pack must not do a silent nothing under it
  (ADR-0006). Options: (a) the same tenant prefix as `column`, read from the same `ITenantContext`, with the same
  integration test; (b) refuse `azure-blob` with `schema-per-tenant` in one sentence until a pilot has it. A catalog
  refusal would name `backend.blob-storage` in an option's `conflicts`, which C2's lint forbids for a changeable
  dimension, so (b) also needs that lint amended. **Decided: (a)**: the blob name is the only place a tenant can
  go, whichever way the database separates tenants.
- **O4. Where the SDK may be named.** #602 proposed "only from Infrastructure and BuildingBlocks"; PharmaTraining's
  rule is stricter, BuildingBlocks only (`BlobStorageSdkStaysInBuildingBlocks`), and fabric-pilot's store is in the
  module's Infrastructure. **Decided:** BuildingBlocks only. With a building block every module uses, a module that
  names the SDK can only be building a second, unscoped client, which is how B2 and `ModulesUseOnlyTheirOwnContainer`
  would be bypassed.
- **O5. Overwrite.** fabric-pilot's upload replaces a blob; PharmaTraining's upload URL is create-only.
  **Decided:** create-only for both operations, names being module-generated, so that a name, once written, always
  holds the same bytes; a module that replaces a file writes it under a new name.

## Consequences

- fabric-pilot and PharmaTraining each replace a hand-written store through `fabric change` (X1) and record the
  migration in a PR of their own. fabric-pilot's names change with B2 before that; files already stored under the old
  names are that pilot's migration to make, not the generator's.
- The deployed storage account, its role assignments and, with signed URLs, its CORS rule for the deployed web origin
  belong to `deploy.target` on Aspire's publisher (D2, M5-07), not to this pack. Aspire's publish-mode role
  assignments are account-wide, so until M5-07 narrows them, a container per module is enforced by configuration and
  by the store's shape, not by the storage service (PharmaTraining ADR 0002).
- Under O1 the pack does not yet replace PharmaTraining's signed URLs: until they are added, its `IBlobUrls` stays
  its own code beside the generated store when it moves through `fabric change`.
- A file two modules need is stored twice, once in each module's container, as under PharmaTraining's decision (c).
- Azurite adds a container to every local run and to the integration fixture; PharmaTraining measured no startup cost
  (PR #124).
- `azure-blob` enters the matrix with every tenancy and topology option it does not refuse (ADR-0003).

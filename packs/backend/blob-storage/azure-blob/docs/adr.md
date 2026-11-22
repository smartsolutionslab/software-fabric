{{~ tenanted = project["backend.tenancy"] == "column" || project["backend.tenancy"] == "schema-per-tenant"
services = project["backend.topology"] == "services" ~}}
# Blob storage: Azure Blob Storage

**Status:** Accepted (generated)

## Decision

The project keeps files by name outside its database, in Azure Blob Storage: Azurite locally, a storage account when deployed (ADR-0020 of the generator, drawn from two pilots' hand-written stores).

- **The AppHost** adds one account, `storage`, run as the Azurite emulator with a data volume, so files outlive a restart as the database's rows do.
{{~ if services ~}}
- **A container per service.** In its loop over the services the AppHost adds the module's container, `<module>-blobs` (the container is named after the module), and hands each service its own as the connection string `blobs`, and no other.
{{~ else ~}}
- **A container per module**, as a module's tables are in a schema of its own. The host reads the account as the connection string `blobs`; a module's store uses the container named after the module (`order-handling` for `OrderHandling`) and creates it on its first write, so no shared file names a module.
{{~ end ~}}
- **The store**, `ModuleBlobStore<TModule>` in `src/BuildingBlocks/Blobs`, offers two operations: store a stream under a name, and open one as a stream, nothing when the name holds nothing. `TModule` is a type of the module's own; no operation takes a container, and none copies between containers. Names are `BlobName`s: relative, at most 1,024 characters, with no empty, `.` or `..` segment, no backslash and no control character.
- **Create-only.** A second write to a name is refused, so a name, once written, always holds the same bytes; a module that replaces a file writes it under a new name.
{{~ if tenanted ~}}
- **The tenant is in every name, and the store puts it there.** The store reads the tenant from `ITenantContext`, the source the module's DbContext reads, never from its caller, and keeps a file under `<tenant>/<name>`: a tenant opens only its own files, whatever name it gives, the full name of another tenant's file included. Without a tenant the store opens nothing and writes nothing. The store is scoped, as that source is.{{ if project["backend.tenancy"] == "schema-per-tenant" }} The database separates tenants by schema; a blob name is the only place a tenant can go, so the prefix is the same as under a tenant column.{{ end }}
{{~ end ~}}
- **The connection** is Azurite's connection string locally and, deployed, the account's blob endpoint, reached with the app's Azure identity (`DefaultAzureCredential`) rather than a key. It is read when a store first needs its container; registering connects to nothing.
- **Only `src/BuildingBlocks/Blobs` names the Azure Storage SDK.** A module takes its store; a module that named the SDK could only build a second, unscoped client{{ if tenanted }}, which is how the tenant in every name would be bypassed{{ end }}.

## Consequences

- An architecture rule fails the build when a module references the Azure Storage SDK (`Azure.Storage.*`), and another when a module's type uses `ModuleBlobStore<TModule>` over another module's type or a generic parameter, or names `BlobContainers`, naming the type and both modules.
- `tests/Integration.Tests/BlobStoreTests.cs` runs the store against the application's Azurite: a file comes back unchanged and a second write is refused{{ if tenanted }}; one tenant cannot read another tenant's file by the full name it is stored under, and without a tenant nothing is opened or written{{ end }}. `tests/Blobs.Tests` checks names and connections, and reads from the AppHost's model, without starting it, that Azurite keeps its files in a volume{{ if services }} and that every service reads its own container as `blobs` and no other{{ end }}.
- There are no signed upload or read URLs yet: every file passes through the host. They follow once a pilot has signed one in Azure, where the account has no key to sign with (ADR-0020 O1). A module that accepts an upload checks its size and content type before it stores it.
- A file two modules need is stored twice, once in each module's container; a module that needs another's file is told through an integration event.
- The deployed storage account, its role assignment for the app's identity (Storage Blob Data Contributor) and its network rules belong to the deployment, not to this pack. Aspire's role assignments are account-wide, so a container per module is enforced by the store's shape and the architecture rules, not by the storage service.
- `fabric change --set backend.blob-storage=none` removes the store again; files already stored are the project's to move.

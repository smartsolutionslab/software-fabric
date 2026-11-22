{{~ tenanted = project["backend.tenancy"] == "column" || project["backend.tenancy"] == "schema-per-tenant" ~}}
## Blob storage: Azure Blob Storage

- A module keeps files through its own `ModuleBlobStore<TModule>` (`src/BuildingBlocks/Blobs`), with a type of its own module as `TModule`, taken by constructor in Infrastructure; give Application an interface of its own if a use case needs it. Take the store, never a `BlobServiceClient`, `BlobContainers` or the Azure Storage SDK: only `src/BuildingBlocks/Blobs` names it, and the architecture rules `BlobStorageSdkStaysInBuildingBlocks` and `ModulesUseOnlyTheirOwnContainer` fail the build otherwise.
- The module makes its blob names itself, with a Guid v7 segment (`BlobName.From($"invoices/{invoice}/{Guid.CreateVersion7()}")`), never from user input such as a file name; keep the file name, if it matters, in the module's own table.
- A write is create-only: a second `UploadAsync` to a name throws. To replace a file, store it under a new name and record the new name.
- A module that accepts an upload checks its size and content type before it stores it or records it.
- Another module's files come through `src/Contracts` (an integration event saying a file exists), never through its container; a file two modules need is stored in each module's own container.
{{~ if tenanted ~}}
- Every name belongs to the current tenant: the store adds it from `ITenantContext`, never from its caller, so never put a tenant into a name yourself. In consumers and background work, open `using var tenant = TenantScope.Begin(message.Metadata);` before storing or opening a file; without a tenant the store opens nothing and refuses to write.
{{~ end ~}}
- The connection is `blobs` from Aspire: Azurite locally, the account's endpoint with the app's identity when deployed. Never put an account key or a connection string in a settings file or in code.
- Test a module's use of files against an in-memory stand-in of its own Application interface; the store itself is tested against Azurite in `tests/Integration.Tests/BlobStoreTests.cs`.

# Blob storage: none

**Status:** Accepted (generated)

## Decision

The project stores no files outside its database (ADR-0020 of the generator). No module references the Azure Storage SDK, and the AppHost runs no storage emulator.

## Consequences

- An architecture rule fails the build when a module references the Azure Storage SDK (`Azure.Storage.*`).
- Storing files later is a project-wide change: `fabric change --set backend.blob-storage=azure-blob` (ADR-0016 C1) brings Azurite to the AppHost, the store in `src/BuildingBlocks/Blobs` with a container per module, and its gates. An SDK added to one module by hand skips all of them, the tenant in every name among them.

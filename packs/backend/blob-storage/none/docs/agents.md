## Blob storage: none

- The project stores no files outside its database. Do not add the Azure Storage SDK (`Azure.Storage.*`) to a module; an architecture rule fails the build if one appears.
- A feature that needs to keep files is a project decision (`fabric change --set backend.blob-storage=azure-blob`): escalate it with `needs:decision` instead of starting it in one module.

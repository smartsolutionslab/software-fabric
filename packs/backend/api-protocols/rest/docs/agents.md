## API protocol: REST with OpenAPI

- Every endpoint has typed request and response models so the OpenAPI document (`/openapi/v1.json`) describes it fully; the frontend client is generated from that document.
- Use nouns and HTTP verbs: `GET /billing/invoices/{id}`, `POST /billing/invoices`. Return `201` with a location for creates.

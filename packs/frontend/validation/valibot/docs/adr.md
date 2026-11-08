# Validation: Valibot

**Status:** Accepted (generated)

## Decision

Schemas are Valibot; types are inferred from them (`v.InferOutput<typeof schema>`) instead of written twice. Valibot is a set of small functions, one per type, check and transform, so the bundle keeps only those a schema uses, also through `import * as v`.

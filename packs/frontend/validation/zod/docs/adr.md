# Validation: Zod

**Status:** Accepted (generated)

## Decision

Schemas are Zod; types are inferred from them (`z.infer<typeof schema>`) instead of written twice.

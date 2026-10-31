## Validation: Zod

- Define the schema, infer the type: `type Invoice = z.infer<typeof invoiceSchema>`.
- Validate untrusted input (forms, URL parameters, storage) with `schema.parse` / `safeParse` at the edge.

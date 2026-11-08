## Validation: Valibot

- Import Valibot as `import * as v from 'valibot'`. Define the schema, infer the type: `type Invoice = v.InferOutput<typeof invoiceSchema>`.
- Checks and transforms go in a pipe: `v.pipe(v.string(), v.trim(), v.minLength(1, 'Enter a name'))`.
- Validate untrusted input (forms, URL parameters, storage) with `v.parse` / `v.safeParse` at the edge.

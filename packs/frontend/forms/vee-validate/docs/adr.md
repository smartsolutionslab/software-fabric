# Forms: vee-validate

**Status:** Accepted (generated)

## Decision

Forms use vee-validate 4 (`useForm`, `defineField`) with the validation library's schema. The schema reaches vee-validate through `toTypedSchema` in `src/forms/toTypedSchema.ts`, a small adapter over zod 4's `safeParseAsync` that the project owns: `@vee-validate/zod` 4.15.1 supports zod 3 only, and the vee-validate release that reads zod 4 directly (Standard Schema) is still a beta. When a stable vee-validate reads zod 4 itself, the adapter can go.

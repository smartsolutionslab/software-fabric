# Forms: vee-validate

**Status:** Accepted (generated)

## Decision

Forms use vee-validate 4 (`useForm`, `defineField`) with the validation library's schema. The schema reaches vee-validate through `toTypedSchema` in `src/forms/toTypedSchema.ts`, {{~ if project["frontend.validation"] == "valibot" }} a small adapter over Valibot's `safeParseAsync` that the project owns, so that forms import `toTypedSchema` from `@/forms/toTypedSchema` whatever the schema library. `@vee-validate/valibot` is not used, and lint refuses it. When a stable vee-validate reads Standard Schema directly, the adapter can go.
{{~ else }} a small adapter over zod 4's `safeParseAsync` that the project owns: `@vee-validate/zod` 4.15.1 supports zod 3 only, and the vee-validate release that reads zod 4 directly (Standard Schema) is still a beta. When a stable vee-validate reads zod 4 itself, the adapter can go.
{{~ end ~}}

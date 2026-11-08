{{~ vue = project["frontend.framework"] == "vue" ~}}
{{~ valibot = project["frontend.validation"] == "valibot" ~}}
# Forms: TanStack Form

**Status:** Accepted (generated)

## Decision

Forms use TanStack Form 1.33.5 (`@tanstack/{{ if vue }}vue{{ else }}react{{ end }}-form`), validated with the project's {{ if valibot }}Valibot{{ else }}zod{{ end }} schema as it is: {{ if valibot }}Valibot 1{{ else }}zod 4{{ end }} implements Standard Schema, which TanStack Form reads, so there is no resolver or adapter package. `revalidateLogic()` checks a form when it is first submitted and on every change after that, so a form nobody has submitted shows no errors yet. TanStack Form hands `onSubmit` the values as typed, not the schema's output, so a submit takes the output with {{ if valibot }}`v.parse(schema, value)`{{ else }}`schema.parse(value)`{{ end }}: trimmed, transformed and defaulted.

{{~ vue = project["frontend.framework"] == "vue" ~}}
{{~ valibot = project["frontend.validation"] == "valibot" ~}}
## Forms: TanStack Form

- `useForm({ defaultValues, validationLogic: revalidateLogic(), validators: { onDynamic: schema }, onSubmit })` from `@tanstack/{{ if vue }}vue{{ else }}react{{ end }}-form`, with a default value for every field. The schema is the {{ if valibot }}Valibot{{ else }}zod{{ end }} schema itself; no resolver or adapter.
{{~ if vue ~}}
- Render each input in `<form.Field v-slot="{ field }" name="...">`, bound with `:value="field.state.value"`, `@input` calling `field.handleChange(...)` and `@blur="field.handleBlur"`; show `field.state.meta.errors[0]?.message` next to it with `role="alert"`, or read it in the script with `form.useStore((state) => state.fieldMeta.name?.errors[0]?.message)`.
{{~ else ~}}
- Render each input in `<form.Field name="...">{(field) => ...}</form.Field>`, bound to `field.state.value`, `field.handleChange` and `field.handleBlur`; show `field.state.meta.errors[0]?.message` next to it with `role="alert"`.
{{~ end ~}}
- The form element's submit handler prevents the default and calls `form.handleSubmit()`. `onSubmit` gets the values as typed: take the schema's output (trimmed, transformed) with {{ if valibot }}`v.parse(schema, value)`{{ else }}`schema.parse(value)`{{ end }}.
- One schema per form, next to the form; reuse it for the API request type where they match. Do not install {{ if vue }}vee-validate or FormKit{{ else }}React Hook Form, a resolver package or Formik{{ end }} (lint refuses them).

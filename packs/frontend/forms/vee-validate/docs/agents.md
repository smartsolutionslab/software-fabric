## Forms: vee-validate

- `useForm({ validationSchema: toTypedSchema(schema), initialValues: { ... } })` with `toTypedSchema` from `@/forms/toTypedSchema`, and an initial value for every field, so an untouched field fails with the schema's message and not as missing; bind inputs with `const [name, nameAttributes] = defineField('name')` and `v-model="name" v-bind="nameAttributes"`, and show `errors.name` next to the field with `role="alert"`.
- Submit through `handleSubmit((values) => ...)`; `values` is the schema's output, already trimmed and coerced.
- One schema per form, next to the form; reuse it for the API request type where they match. Do not install `@vee-validate/zod`, FormKit or TanStack Form (lint refuses them).

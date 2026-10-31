## Forms: React Hook Form

- `useForm({ resolver: zodResolver(schema) })`; register inputs with `{...register('field')}` and show `formState.errors` next to the field with `role="alert"`.
- One schema per form, next to the form; reuse it for the API request type where they match.

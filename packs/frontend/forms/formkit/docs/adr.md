{{~ valibot = project["frontend.validation"] == "valibot" ~}}
# Forms: FormKit

**Status:** Accepted (generated)

## Decision

Forms use FormKit 2.5.0 (`@formkit/vue`): a `<FormKit type="form">` holding `<FormKit>` inputs, which render their own label, input and messages. FormKit is installed once, in `src/providers/formkit.ts`, with the project's `src/forms/formkit.config.ts`, and component tests install it the same way (`src/test-setup.ts`).

- **The look is the project's.** No FormKit theme (`@formkit/themes`, Genesis) is used. `src/forms/formkit.config.ts` gives each section of an input (outer, wrapper, label, input, help, messages, message) the classes {{ if project["frontend.ui-library"] == "reka-ui" }}of Tailwind's slate palette that `BaseButton` uses{{ else }}shadcn-vue's Input, Textarea and Label use, in the theme's colours from `src/index.css`{{ end }}, so a form looks like the rest of the app and there is one place to change it. It also renders each message as a paragraph with `role="alert"`. Submit buttons are the project's own buttons, not FormKit's (`:actions="false"`).
- **The schema is the only validation.** FormKit's string rules (`validation="required|length:…"`) are not used. A form's `@submit` is `schemaSubmit(schema, handler)` from `src/forms/schemaSubmit.ts`, a small adapter the project owns: it runs the {{ if valibot }}Valibot{{ else }}zod{{ end }} schema with {{ if valibot }}`v.safeParseAsync`{{ else }}`safeParseAsync`{{ end }}, puts each message on the field its issue names with `node.setErrors` (dotted paths, list items by index), and hands the handler the schema's output only when the form is valid. {{ if valibot }}FormKit publishes no Valibot adapter, and lint refuses FormKit's published schema adapter, as it refuses vee-validate and TanStack Form.{{ else }}`@formkit/zod` 2.5.0 supports zod 3 only, so lint refuses it, as it refuses vee-validate and TanStack Form.{{ end }}
- `@formkit/core` is a direct dependency, at the same version, for the `FormKitNode` type and the adapter's test.

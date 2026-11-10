## UI components: Vuetify

- Compose screens from Vuetify's components, imported where they are used (`import { VBtn, VTable } from 'vuetify/components'`), and lay out with Vuetify's utility classes (`class="d-flex align-center ga-2 pa-4"`).
- Name colours through the theme (`color="primary"` on a component; `text-medium-emphasis`, `text-error` and `bg-primary` as classes), never as values written in a component.
- Form fields stay labelled native controls (`<label>` around `<input class="rounded border px-2 py-1">`), so the form library binds them as they are.
- A component test renders under the app's Vuetify instance by itself (`src/test-setup.ts`); do not install Vuetify again in a test.
- Do not install PrimeVue, Quasar, Element Plus, Ant Design Vue, Naive UI, Headless UI, Radix Vue or Reka UI (lint refuses them).

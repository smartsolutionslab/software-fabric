import { createPinia } from 'pinia';
import type { App, Component } from 'vue';

/** Client state lives in Pinia stores (src/stores/); server data does not, it stays in TanStack Query's cache. */
export const withPinia = (app: App, root: Component) => {
  app.use(createPinia());
  return root;
};

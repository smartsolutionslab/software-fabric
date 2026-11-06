import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import type { App, Component } from 'vue';

/** The one cache of server data; the generated hooks (src/api/generated.ts) read and invalidate it. */
const queryClient = new QueryClient();

export const withQueryClient = (app: App, root: Component) => {
  app.use(VueQueryPlugin, { queryClient });
  return root;
};

import type { Component } from 'vue';

/** What src/modules/<module>/routes.ts exports as `route`; the app finds every module's file by itself (no list to edit). */
export interface ModuleRoute {
  path: string;
  title: string;
  component: Component;
}

export const moduleRoutes = Object.values(
  import.meta.glob<ModuleRoute>('./*/routes.ts', { eager: true, import: 'route' }),
);

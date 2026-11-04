import type { App, Component } from 'vue';
import { createWebHistory } from 'vue-router';
import { createAppRouter } from '@/router';

export const withRouter = (app: App, root: Component) => {
  app.use(createAppRouter(createWebHistory()));
  return root;
};

import { nextTick } from 'vue';
import { createRouter, type Router, type RouterHistory, START_LOCATION } from 'vue-router';
import { moduleRoutes } from '@/modules';
import Home from '@/pages/Home.vue';

/** The start page, then every module's route; tests pass createMemoryHistory(), the app createWebHistory(). */
export function createAppRouter(history: RouterHistory) {
  return inViewTransitions(
    createRouter({
      history,
      routes: [
        { path: '/', component: Home },
        ...moduleRoutes.map((route) => ({ path: `/${route.path}`, component: route.component })),
      ],
    }),
  );
}

/**
 * Each navigation after the first, inside the View Transitions API where the browser has it (docs/adr): the browser
 * captures the page, the navigation goes on, and the browser animates to the new page once it has rendered. Without
 * the API the navigation goes on as it did.
 */
function inViewTransitions(router: Router) {
  router.beforeResolve((_to, from) => {
    if (typeof document.startViewTransition !== 'function' || from === START_LOCATION) {
      return true;
    }
    return new Promise<true>((resolve) => {
      document.startViewTransition(
        () =>
          new Promise<void>((rendered) => {
            const stop = router.afterEach(async () => {
              stop();
              await nextTick();
              rendered();
            });
            resolve(true);
          }),
      );
    });
  });
  return router;
}

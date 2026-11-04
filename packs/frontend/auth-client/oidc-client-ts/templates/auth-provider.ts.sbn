import { UserManager } from 'oidc-client-ts';
import { type App, type Component, h } from 'vue';
import { oidcConfig } from '@/auth/oidcConfig';
import SignedIn from '@/auth/SignedIn.vue';
import { authKey, createAuth } from '@/auth/useAuth';

/** Signs the user in through the identity provider, and renders the app inside SignedIn. */
export const withAuth = (app: App, root: Component): Component => {
  app.provide(authKey, createAuth(new UserManager(oidcConfig)));
  return { render: () => h(SignedIn, null, { default: () => h(root) }) };
};

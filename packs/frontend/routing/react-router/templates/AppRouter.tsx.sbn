import { useState, type ReactNode } from 'react';
import { createBrowserRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';

/**
 * The app in a data router, the kind whose links can ask for the View Transitions API (docs/adr). src/App.tsx draws
 * every route, so the router has one route, which matches any address. It is created when it first renders, not when
 * the providers are composed: it reads the address once, and the providers before it may still change it (a sign-in
 * callback leaves its code there until it is handled).
 */
export function AppRouter({ app }: { app: ReactNode }) {
  const [router] = useState(() => createBrowserRouter([{ path: '*', element: app }]));
  return <RouterProvider router={router} />;
}

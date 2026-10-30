import { type ReactNode, useEffect } from 'react';
import { hasAuthParams, useAuth } from 'react-oidc-context';

/** Renders its children for a signed-in user and sends everyone else to the identity provider. */
export function SignedIn({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const mustSignIn = !auth.isAuthenticated && !auth.isLoading && !auth.activeNavigator && !auth.error && !hasAuthParams();

  useEffect(() => {
    if (mustSignIn) {
      void auth.signinRedirect();
    }
  }, [mustSignIn, auth]);

  if (auth.error) {
    return <p role="alert">Sign-in failed: {auth.error.message}</p>;
  }
  if (!auth.isAuthenticated) {
    return <p>Signing in…</p>;
  }

  return (
    <>
      <div className="flex items-center justify-end gap-3 p-2 text-sm">
        <span>Signed in as {auth.user?.profile.preferred_username}</span>
        <button type="button" onClick={() => void auth.signoutRedirect()}>
          Sign out
        </button>
      </div>
      {children}
    </>
  );
}

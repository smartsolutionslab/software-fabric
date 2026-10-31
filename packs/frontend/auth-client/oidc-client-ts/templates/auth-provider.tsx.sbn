import type { ReactNode } from 'react';
import { AuthProvider } from 'react-oidc-context';
import { oidcConfig } from '@/auth/oidcConfig';
import { SignedIn } from '@/auth/SignedIn';

export const withAuth = (children: ReactNode) => (
  <AuthProvider {...oidcConfig}>
    <SignedIn>{children}</SignedIn>
  </AuthProvider>
);

import { User } from 'oidc-client-ts';
import { oidcConfig } from './oidcConfig';

/** The signed-in user's access token, read where react-oidc-context stores it (code outside React needs it). */
export function accessToken(): string | undefined {
  const stored = sessionStorage.getItem(`oidc.user:${oidcConfig.authority}:${oidcConfig.client_id}`);
  return stored ? User.fromStorageString(stored).access_token : undefined;
}

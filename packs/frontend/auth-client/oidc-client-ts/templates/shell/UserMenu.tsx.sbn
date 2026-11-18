import { LogOutIcon } from 'lucide-react';
import { useAuth } from 'react-oidc-context';
import { useCurrentUser } from '@/api/useCurrentUser';
import { Button } from '@/components/ui/button';

/** The app shell's user menu: who is signed in, as the host's /me names them, and Sign out. */
export function UserMenu() {
  const auth = useAuth();
  const name = useCurrentUser();
  return (
    <div className="flex items-center justify-between gap-2 px-2 text-sm">
      <span className="truncate">{name === undefined ? 'Signed in' : `Signed in as ${name}`}</span>
      <Button variant="ghost" size="sm" onClick={() => void auth.signoutRedirect()}>
        <LogOutIcon />
        Sign out
      </Button>
    </div>
  );
}

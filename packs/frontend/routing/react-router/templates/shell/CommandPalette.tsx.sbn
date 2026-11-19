import { SearchIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { Button } from '@/components/ui/button';
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandShortcut,
} from '@/components/ui/command';
import type { ModuleRoute } from '@/modules';

/**
 * Goes to any page from the keyboard: Ctrl+K (Cmd+K on a Mac) opens it, typing narrows the pages, the arrow keys pick
 * one and Enter goes there. The button beside the breadcrumbs opens it too.
 */
export function CommandPalette({ routes }: { routes: readonly ModuleRoute[] }) {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        setOpen((wasOpen) => !wasOpen);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
    };
  }, []);

  const go = (to: string) => {
    setOpen(false);
    void navigate(to, { viewTransition: true });
  };

  return (
    <>
      <Button
        variant="outline"
        size="sm"
        className="ml-auto text-muted-foreground"
        aria-keyshortcuts="Control+K Meta+K"
        onClick={() => setOpen(true)}
      >
        <SearchIcon />
        Go to…
        <CommandShortcut>Ctrl K</CommandShortcut>
      </Button>
      <CommandDialog open={open} onOpenChange={setOpen} title="Go to" description="Pick a page to go to.">
        <CommandInput placeholder="Go to…" />
        <CommandList label="Pages">
          <CommandEmpty>No page matches.</CommandEmpty>
          <CommandGroup heading="Pages">
            <CommandItem value="Home" onSelect={() => go('/')}>
              Home
            </CommandItem>
            {routes.map((route) => (
              <CommandItem key={route.path} value={route.title} onSelect={() => go(`/${route.path}`)}>
                {route.title}
              </CommandItem>
            ))}
          </CommandGroup>
        </CommandList>
      </CommandDialog>
    </>
  );
}

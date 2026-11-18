import type { ReactNode } from 'react';

interface EmptyStateProps {
  /** An icon (`<Inbox />` from lucide-react); it is decoration, so screen readers skip it. */
  icon: ReactNode;
  /** One sentence: what is missing. */
  children: ReactNode;
  /** The primary action that fills the list, such as a `<Button>`. */
  action?: ReactNode;
}

/** What a list shows when it has nothing to show: an icon, one sentence and the action that fills it. */
export function EmptyState({ icon, children, action }: EmptyStateProps) {
  return (
    <div data-slot="empty-state" className="flex flex-col items-center gap-3 py-8 text-center">
      <div aria-hidden="true" className="text-muted-foreground">
        {icon}
      </div>
      <p className="text-body text-muted-foreground">{children}</p>
      {action}
    </div>
  );
}

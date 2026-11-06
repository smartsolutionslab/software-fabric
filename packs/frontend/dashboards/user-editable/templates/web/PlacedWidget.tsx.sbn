import { useGetDashboardsWidgetDeclarationsByIdQuery } from '@/api/generated';
import { Button } from '@/components/ui/button';
import type { Placement } from './layout';
import { WidgetView } from './WidgetView';

interface PlacedWidgetProps {
  placement: Placement;
  rowHeight: number;
  /** Goes up with every save of the dashboard, so the widget's data is read again. */
  refresh: number;
  onRemove: () => void;
}

/**
 * A widget on the dashboard, titled by its own declaration rather than by the page of the widget picker on screen; its
 * id stands in until the declaration arrives, or when the widget is no longer declared for the user.
 */
export function PlacedWidget({ placement, rowHeight, refresh, onRemove }: PlacedWidgetProps) {
  const { data: declaration } = useGetDashboardsWidgetDeclarationsByIdQuery({ id: placement.source });
  const title = declaration?.title ?? placement.source;

  return (
    <section aria-label={title} className="flex h-full flex-col overflow-hidden rounded border bg-card p-2">
      <header className="flex items-center justify-between gap-2">
        <h2 className="text-sm font-medium">{title}</h2>
        <Button type="button" variant="ghost" size="sm" aria-label={`Remove ${title}`} onClick={onRemove}>
          Remove
        </Button>
      </header>
      <div className="min-h-0 flex-1 overflow-auto">
        <WidgetView
          source={placement.source}
          visual={placement.visual}
          title={title}
          height={Math.max(120, placement.height * rowHeight - 60)}
          refresh={refresh}
        />
      </div>
    </section>
  );
}

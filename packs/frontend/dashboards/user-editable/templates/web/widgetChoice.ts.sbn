import type { WidgetDescriptionDto } from '@/api/generated';

/** The ways each kind of widget can be shown; the host refuses any other pairing. */
export const visuals = {
  Chart: [
    { value: 'bar', label: 'Bar' },
    { value: 'line', label: 'Line' },
    { value: 'pie', label: 'Pie' },
  ],
  Table: [{ value: 'table', label: 'Table' }],
} as const;

export type Choice = { ok: true } | { ok: false; field: 'source' | 'visual'; message: string };

/**
 * Whether a widget chosen in the picker can be added as it stands: the catalogue page on screen lists it and the visual
 * suits its kind. A choice made from a page that has since changed is refused instead of saved for the host to reject.
 */
export function checkChoice(
  catalog: readonly Pick<WidgetDescriptionDto, 'id' | 'kind'>[],
  source: string,
  visual: string,
): Choice {
  const widget = catalog.find((listed) => listed.id === source);
  if (!widget) {
    return { ok: false, field: 'source', message: 'That widget is no longer offered; choose another.' };
  }

  if (!visuals[widget.kind].some((suited) => suited.value === visual)) {
    return { ok: false, field: 'visual', message: 'That widget cannot be shown that way; choose how to show it.' };
  }

  return { ok: true };
}

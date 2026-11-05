import type { WidgetPlacement } from '@/api/generated';

/** A widget on the grid, with the numbers the generated client types as number or string made plain numbers. */
export interface Placement {
  key: string;
  source: string;
  visual: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** One item of react-grid-layout's layout: the widget's key and its place in grid units. */
export interface GridItem {
  i: string;
  x: number;
  y: number;
  w: number;
  h: number;
  maxH?: number;
}

/** The grid has 12 columns; the host refuses a widget that does not fit them or is taller than this. */
export const columns = 12;
export const tallest = 24;

/** The lowest row a widget may start on; the host refuses a dashboard with a widget below it. */
export const lowest = 1000;

export const fromSaved = (widgets: readonly WidgetPlacement[]): Placement[] =>
  widgets.map(({ key, source, visual, x, y, width, height }) => ({
    key,
    source,
    visual,
    x: Number(x),
    y: Number(y),
    width: Number(width),
    height: Number(height),
  }));

export const toLayout = (placements: readonly Placement[]): GridItem[] =>
  placements.map(({ key, x, y, width, height }) => ({ i: key, x, y, w: width, h: height, maxH: tallest }));

/** The placements moved and resized as the grid reports them; a placement the layout does not name stays as it is. */
export function applyLayout(placements: readonly Placement[], layout: readonly GridItem[]): Placement[] {
  const byKey = new Map(layout.map((item) => [item.i, item]));
  return placements.map((placement) => {
    const item = byKey.get(placement.key);
    return item ? { ...placement, x: item.x, y: item.y, width: item.w, height: item.h } : placement;
  });
}

/** Where a new widget goes: on the left, below everything already on the grid, half the grid wide. */
export function nextPlacement(
  placements: readonly Placement[],
  key: string,
  source: string,
  visual: string,
): Placement {
  const bottom = Math.max(0, ...placements.map((placement) => placement.y + placement.height));
  return { key, source, visual, x: 0, y: bottom, width: columns / 2, height: 4 };
}

/** Whether a new widget, which goes below everything on the grid, would still start at or above the lowest row. */
export const hasRoom = (placements: readonly Placement[]): boolean =>
  Math.max(0, ...placements.map((placement) => placement.y + placement.height)) <= lowest;

/** Whether every widget starts at or above the lowest row, as the host requires. */
export const withinReach = (placements: readonly Placement[]): boolean =>
  placements.every((placement) => placement.y <= lowest);

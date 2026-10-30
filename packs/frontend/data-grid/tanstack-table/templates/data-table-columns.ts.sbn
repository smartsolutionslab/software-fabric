import { createColumnHelper, type RowData, tableFeatures } from '@tanstack/react-table';

/** Core features only; register sorting, filtering or paging here when a screen needs them. */
export const features = tableFeatures({});

export type Features = typeof features;

/** The column helper for rows of TRow: `const notes = columnsOf<Note>()`, then `notes.columns([notes.accessor('text', ...)])`. */
export const columnsOf = <TRow extends RowData>() => createColumnHelper<Features, TRow>();

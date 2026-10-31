## Data grid: TanStack Table

- Lists of records use `DataTable` with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`).
- Define columns at module scope, and give `data` a stable fallback (`const none: Row[] = []`), not `?? []` inline.
- Need sorting or paging? Register the feature once in `data-table-columns.ts` and render it in `DataTable`.

{{~ if project["frontend.framework"] == "vue" ~}}
## Data grid: TanStack Table

- Lists of records use `DataTable` (`@/components/ui/DataTable.vue`) with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`).
- Define columns at module scope (they are read once), and pass `data` as a ref or computed with a stable fallback (`const none: Row[] = []`, then `computed(() => data.value ?? none)`).
- Need sorting or paging? Register the feature once in `data-table-columns.ts` and render it in `DataTable.vue`.
{{~ else ~}}
## Data grid: TanStack Table

- Lists of records use `DataTable` with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`).
- Define columns at module scope, and give `data` a stable fallback (`const none: Row[] = []`), not `?? []` inline.
- Need sorting or paging? Register the feature once in `data-table-columns.ts` and render it in `DataTable`.
{{~ end ~}}

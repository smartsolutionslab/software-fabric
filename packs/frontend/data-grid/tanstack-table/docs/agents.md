{{~ if project["frontend.framework"] == "vue" ~}}
## Data grid: TanStack Table

- Lists of records use `DataTable` (`@/components/ui/DataTable.vue`) with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`).
- Define columns at module scope (they are read once), and pass `data` as a ref or computed with a stable fallback (`const none: Row[] = []`, then `computed(() => data.value ?? none)`).
- Need sorting or paging? Register the feature once in `data-table-columns.ts` and render it in `DataTable.vue`.
{{~ if project["frontend.ui-library"] == "shadcn-vue" ~}}
- While the query is pending, pass `:loading="isPending"`: the table is busy, says "Loading…" to screen readers and shows skeleton rows in its own shape. Fill its `empty` slot with an `EmptyState` (`@/components/ui/empty-state`), which it shows when it has no rows. Never put a `Loading…` paragraph or an empty table in its place.
{{~ end ~}}
{{~ else ~}}
## Data grid: TanStack Table

- Lists of records use `DataTable` with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`).
- Define columns at module scope, and give `data` a stable fallback (`const none: Row[] = []`), not `?? []` inline.
- Need sorting or paging? Register the feature once in `data-table-columns.ts` and render it in `DataTable`.
{{~ if project["frontend.ui-library"] == "shadcn" ~}}
- While the query is pending, pass `loading={isLoading}`: the table is busy, says "Loading…" to screen readers and shows skeleton rows in its own shape. Give `empty` an `EmptyState` (`@/components/ui/empty-state`), which it shows when it has no rows: `<DataTable columns={columns} data={data ?? none} loading={isLoading} empty={<EmptyState icon={<Inbox />} action={…}>No invoices yet.</EmptyState>} />`. Never put a `Loading…` paragraph or an empty table in its place.
{{~ end ~}}
{{~ end ~}}

{{~ if project["frontend.framework"] == "vue" ~}}
## Data grid: TanStack Table

- Lists of records use `DataTable` (`@/components/ui/DataTable.vue`) with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`).
- Define columns at module scope (they are read once), and pass `data` as a ref or computed with a stable fallback (`const none: Row[] = []`, then `computed(() => data.value ?? none)`).
- Need sorting or paging? Register the feature once in `data-table-columns.ts` and render it in `DataTable.vue`.
{{~ if project["frontend.ui-library"] == "shadcn-vue" ~}}
- While the query is pending, pass `:loading="isPending"`: the table is busy, says "Loading…" to screen readers and shows skeleton rows in its own shape. Fill its `empty` slot with an `EmptyState` (`@/components/ui/empty-state`), which it shows when it has no rows. Never put a `Loading…` paragraph or an empty table in its place.
- Pass a list's `PagedResult` as `:paging="page"`: the table says below its rows "Showing 1–25 of 312", which screen readers hear as its description. Never write that line yourself.
- Put a row's actions (edit, delete) in the `actions` slot, `<template #actions="{ row }">…</template>`: they fill the row's last cell, under a header screen readers hear as "Actions", and show while the row is hovered or has focus. Give each a name that says which row it acts on (`Edit ${row.name}`).
- The table has its own density toggle and Columns control, and its header stays in view while the page scrolls: never add your own, or wrap it in an element that scrolls, which stops the header sticking to the page.
{{~ end ~}}
{{~ else ~}}
## Data grid: TanStack Table

- Lists of records use `DataTable` with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`).
- Define columns at module scope, and give `data` a stable fallback (`const none: Row[] = []`), not `?? []` inline.
- Need sorting or paging? Register the feature once in `data-table-columns.ts` and render it in `DataTable`.
{{~ if project["frontend.ui-library"] == "shadcn" ~}}
- While the query is pending, pass `loading={isLoading}`: the table is busy, says "Loading…" to screen readers and shows skeleton rows in its own shape. Give `empty` an `EmptyState` (`@/components/ui/empty-state`), which it shows when it has no rows: `<DataTable columns={columns} data={data ?? none} loading={isLoading} empty={<EmptyState icon={<Inbox />} action={…}>No invoices yet.</EmptyState>} />`. Never put a `Loading…` paragraph or an empty table in its place.
- Pass a list's `PagedResult` as `paging={page}`: the table says below its rows "Showing 1–25 of 312", which screen readers hear as its description. Never write that line yourself.
- Give a row's actions (edit, delete) as `actions={(invoice) => <Button …>Edit {invoice.number}</Button>}`: they fill the row's last cell, under a header screen readers hear as "Actions", and show while the row is hovered or has focus. Give each a name that says which row it acts on.
- The table has its own density toggle and Columns control, and its header stays in view while the page scrolls: never add your own, or wrap it in an element that scrolls, which stops the header sticking to the page.
{{~ end ~}}
{{~ end ~}}

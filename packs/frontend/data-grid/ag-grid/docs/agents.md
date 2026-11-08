{{~ if project["frontend.framework"] == "vue" ~}}
## Data grid: AG Grid (community)

- Lists of records use `DataTable` (`@/components/ui/DataTable.vue`) with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`): `rows.accessor('field', { header })`, or `rows.accessor((row) => ..., { id, header })` for a computed column.
- Define columns at module scope (they are read once), and pass `data` as a ref or computed with a stable fallback (`const none: Row[] = []`, then `computed(() => data.value ?? none)`).
- Need sorting, filtering or paging? Register AG Grid's module once in `modules` in `data-table-columns.ts`. Community modules only; never add `ag-grid-enterprise` without an ADR.
- AG Grid renders after mounting: in a component test, wait for the first cell or header with `findByRole`.
{{~ else ~}}
## Data grid: AG Grid (community)

- Lists of records use `DataTable` with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`): `rows.accessor('field', { header })`, or `rows.accessor((row) => ..., { id, header })` for a computed column.
- Define columns at module scope, and give `data` a stable fallback (`const none: Row[] = []`), not `?? []` inline.
- Need sorting, filtering or paging? Register AG Grid's module once in `modules` in `data-table-columns.ts`. Community modules only; never add `ag-grid-enterprise` without an ADR.
- AG Grid renders after mounting: in a component test, wait for the first cell or header with `findByRole`.
{{~ end ~}}

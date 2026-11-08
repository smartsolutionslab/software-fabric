{{~ if project["frontend.framework"] == "vue" ~}}
# Data grid: AG Grid (community)

**Status:** Accepted (generated)

## Decision

Tables are AG Grid's community edition (v36, `ag-grid-community` with `ag-grid-vue3`, MIT licensed) rendered by `src/components/ui/DataTable.vue`, as high as their rows. Columns come from `columnsOf<Row>()` in `data-table-columns.ts`, the same helper a TanStack Table project has, so a screen is written the same way with either grid. AG Grid's features are modules: only the client-side row model is registered, in `data-table-columns.ts`; a screen that needs sorting, filtering or paging registers that module there, so every table gets it the same way.

The enterprise edition (`ag-grid-enterprise`) needs a commercial licence; adopting it is this project's decision, recorded in its own ADR.
{{~ else ~}}
# Data grid: AG Grid (community)

**Status:** Accepted (generated)

## Decision

Tables are AG Grid's community edition (v36, `ag-grid-community` with `ag-grid-react`, MIT licensed) rendered by `src/components/ui/data-table.tsx`, as high as their rows. Columns come from `columnsOf<Row>()` in `data-table-columns.ts`, the same helper a TanStack Table project has, so a screen is written the same way with either grid. AG Grid's features are modules: only the client-side row model is registered, in `data-table-columns.ts`; a screen that needs sorting, filtering or paging registers that module there, so every table gets it the same way.

The enterprise edition (`ag-grid-enterprise`) needs a commercial licence; adopting it is this project's decision, recorded in its own ADR.
{{~ end ~}}

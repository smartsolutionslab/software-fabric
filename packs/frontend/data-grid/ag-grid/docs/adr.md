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
{{~ end }}
## Theme

{{~ if project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue" }}
The grid uses AG Grid's Quartz theme bound to the theme variables in `src/index.css` (`--background`, `--foreground`, `--border`, `--muted`, `--primary`, and the inherited font), set as `theme` in `data-table-columns.ts`. It follows the app's colours and dark mode, and changes with them in one place.
{{~ else }}
The grid uses AG Grid's Quartz theme as shipped (`theme` in `data-table-columns.ts`): its own colours, font and accent. This is the exception to changing colours in one place, because this UI library defines no theme variables for the grid to follow. When it does, bind Quartz to them with `themeQuartz.withParams` there.
{{~ end ~}}

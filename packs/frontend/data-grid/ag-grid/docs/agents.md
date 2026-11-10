{{~ bound = project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue" ~}}
{{~ mui_theme = project["frontend.ui-library"] == "mui" && project["frontend.styling"] == "library-theme" ~}}
{{~ vuetify = project["frontend.ui-library"] == "vuetify" ~}}
{{~ if project["frontend.framework"] == "vue" ~}}
## Data grid: AG Grid (community)

- Lists of records use `DataTable` (`@/components/ui/DataTable.vue`) with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`): `rows.accessor('field', { header })`, or `rows.accessor((row) => ..., { id, header })` for a computed column.
- Define columns at module scope (they are read once), and pass `data` as a ref or computed with a stable fallback (`const none: Row[] = []`, then `computed(() => data.value ?? none)`).
- Need sorting, filtering or paging? Register AG Grid's module once in `modules` in `data-table-columns.ts`. Community modules only; never add `ag-grid-enterprise` without an ADR.
- AG Grid renders after mounting: in a component test, wait for the first cell or header with `findByRole`.
- {{ if bound }}The grid's colours, borders and font come from the theme variables in `src/index.css` (`theme` in `data-table-columns.ts`); change them there, never per table.{{ else if mui_theme }}The grid's colours, borders and font come from the MUI theme in `src/theme.ts` (`theme` in `data-table-columns.ts`); change them there, never per table.{{ else if vuetify }}The grid's colours, borders and font come from Vuetify's theme variables (`theme` in `data-table-columns.ts`), which Vuetify writes from {{ if project["frontend.styling"] == "library-theme" }}the theme in `src/theme.ts`{{ else }}its theme{{ end }}; change them there, never per table.{{ else }}The grid uses AG Grid's Quartz theme as shipped (`theme` in `data-table-columns.ts`), the one exception to colours in one place (docs/adr); do not restyle it per table.{{ end }}
{{~ else ~}}
## Data grid: AG Grid (community)

- Lists of records use `DataTable` with columns from `columnsOf<Row>()` (`@/components/ui/data-table-columns`): `rows.accessor('field', { header })`, or `rows.accessor((row) => ..., { id, header })` for a computed column.
- Define columns at module scope, and give `data` a stable fallback (`const none: Row[] = []`), not `?? []` inline.
- Need sorting, filtering or paging? Register AG Grid's module once in `modules` in `data-table-columns.ts`. Community modules only; never add `ag-grid-enterprise` without an ADR.
- AG Grid renders after mounting: in a component test, wait for the first cell or header with `findByRole`.
- {{ if bound }}The grid's colours, borders and font come from the theme variables in `src/index.css` (`theme` in `data-table-columns.ts`); change them there, never per table.{{ else if mui_theme }}The grid's colours, borders and font come from the MUI theme in `src/theme.ts` (`theme` in `data-table-columns.ts`); change them there, never per table.{{ else if vuetify }}The grid's colours, borders and font come from Vuetify's theme variables (`theme` in `data-table-columns.ts`), which Vuetify writes from {{ if project["frontend.styling"] == "library-theme" }}the theme in `src/theme.ts`{{ else }}its theme{{ end }}; change them there, never per table.{{ else }}The grid uses AG Grid's Quartz theme as shipped (`theme` in `data-table-columns.ts`), the one exception to colours in one place (docs/adr); do not restyle it per table.{{ end }}
{{~ end ~}}

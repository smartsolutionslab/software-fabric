{{~ if project["frontend.framework"] == "vue" ~}}
# Data grid: TanStack Table

**Status:** Accepted (generated)

## Decision

Tables are TanStack Table (v9, headless, `@tanstack/vue-table`) rendered by `src/components/ui/DataTable.vue`. Features (sorting, filtering, paging) are registered in `data-table-columns.ts` when a screen first needs them, so every table gets them the same way.{{ if project["frontend.ui-library"] == "shadcn-vue" }} While its query is pending (`loading`) the table is busy (`aria-busy`), named "Loading…" for screen readers, and shows three skeleton rows of its own columns, which screen readers skip; with no rows it shows its `empty` slot, an `EmptyState`, in one cell across them. With the accessibility tests, axe checks it in the three states, in both themes.{{ end }}
{{~ else ~}}
# Data grid: TanStack Table

**Status:** Accepted (generated)

## Decision

Tables are TanStack Table (v9, headless) rendered by `src/components/ui/data-table.tsx`. Features (sorting, filtering, paging) are registered in `data-table-columns.ts` when a screen first needs them, so every table gets them the same way.{{ if project["frontend.ui-library"] == "shadcn" }} While its query is pending (`loading`) the table is busy (`aria-busy`), named "Loading…" for screen readers, and shows three skeleton rows of its own columns, which screen readers skip; with no rows it shows what it is given as `empty`, an `EmptyState`, in one cell across them. With the accessibility tests, axe checks it in the three states, in both themes.{{ end }}
{{~ end ~}}

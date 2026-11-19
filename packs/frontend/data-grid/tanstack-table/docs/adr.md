{{~ if project["frontend.framework"] == "vue" ~}}
# Data grid: TanStack Table

**Status:** Accepted (generated)

## Decision

Tables are TanStack Table (v9, headless, `@tanstack/vue-table`) rendered by `src/components/ui/DataTable.vue`. Features (sorting, filtering, paging) are registered in `data-table-columns.ts` when a screen first needs them, so every table gets them the same way.{{ if project["frontend.ui-library"] == "shadcn-vue" }} While its query is pending (`loading`) the table is busy (`aria-busy`), named "Loading…" for screen readers, and shows three skeleton rows of its own columns, which screen readers skip; with no rows it shows its `empty` slot, an `EmptyState`, in one cell across them. With the accessibility tests, axe checks it in the three states, in both themes. Its header stays in view while the page scrolls (`sticky`, so the table sits in no scrolling element of its own); above it, a density toggle (comfortable, compact; `aria-pressed`) and a Columns control (a disclosure of one checkbox per column, closed with Escape, which never hides the last column shown) let a reader shape it; a row's actions sit in its last cell and show while the row is hovered or has focus, and on a touch screen always; given the list's `PagedResult` (`paging`) it says below the rows "Showing 1–25 of 312", the table's accessible description. Its tests drive the controls from the keyboard, and axe checks them open.{{ end }}
{{~ else ~}}
# Data grid: TanStack Table

**Status:** Accepted (generated)

## Decision

Tables are TanStack Table (v9, headless) rendered by `src/components/ui/data-table.tsx`. Features (sorting, filtering, paging) are registered in `data-table-columns.ts` when a screen first needs them, so every table gets them the same way.{{ if project["frontend.ui-library"] == "shadcn" }} While its query is pending (`loading`) the table is busy (`aria-busy`), named "Loading…" for screen readers, and shows three skeleton rows of its own columns, which screen readers skip; with no rows it shows what it is given as `empty`, an `EmptyState`, in one cell across them. With the accessibility tests, axe checks it in the three states, in both themes. Its header stays in view while the page scrolls (`sticky`, so the table sits in no scrolling element of its own); above it, a density toggle (comfortable, compact; `aria-pressed`) and a Columns control (a disclosure of one checkbox per column, closed with Escape, which never hides the last column shown) let a reader shape it; a row's actions sit in its last cell and show while the row is hovered or has focus, and on a touch screen always; given the list's `PagedResult` (`paging`) it says below the rows "Showing 1–25 of 312", the table's accessible description. Its tests drive the controls from the keyboard, and axe checks them open.{{ end }}
{{~ end ~}}

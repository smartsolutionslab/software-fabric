# Data grid: TanStack Table

**Status:** Accepted (generated)

## Decision

Tables are TanStack Table (v9, headless) rendered by `src/components/ui/data-table.tsx`. Features (sorting, filtering, paging) are registered in `data-table-columns.ts` when a screen first needs them, so every table gets them the same way.

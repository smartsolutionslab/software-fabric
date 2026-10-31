# Frontend state: Redux Toolkit

**Status:** Accepted (generated)

## Decision

One Redux store (`src/store.ts`) built with Redux Toolkit. Server data lives in RTK Query's cache, not in hand-written slices; slices hold client state only (UI, selections, drafts). Components use the typed hooks in `src/hooks.ts`.

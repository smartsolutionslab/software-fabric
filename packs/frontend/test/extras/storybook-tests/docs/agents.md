## Storybook tests

- Every story in `web/packages/ui/src/` is a test: `pnpm test` renders it and runs its play function (`src/stories.test.tsx`). Do not list stories there; a new `<Name>.stories.tsx` is picked up by itself.
- Check behaviour in a play function with `expect`, `within` and `userEvent` from `storybook/test`, never by hand in Storybook alone.
- A failing story is fixed in the component or the story, never skipped or removed to make CI pass.

# Test extra: accessibility

**Status:** Accepted (generated)

## Decision

Component tests check rendered screens with axe (`vitest-axe`): `expect(await axe(container)).toHaveNoViolations()`. jsdom cannot judge contrast or layout, so this catches structure (names, roles, labels), not everything; end-to-end axe checks with Playwright follow when screens exist to check.
{{~ if project["frontend.architecture"] == "spa" ~}}

Each module screen's axe check is a file of its own beside it, `<Name>Page.axe.test.*`, which `add-module` generates and which a screen written by hand gets too; `src/screens.test.ts` fails while a screen has none. It always runs, standing in for the generated API client so that it renders the real screen, so an assertion skipped or commented out in the screen's own `<Name>Page.test.*` never leaves the screen unchecked. A screen whose own test also runs axe is checked twice, which is accepted. The screen's own component test stays the project's, so an upgrade never writes over it.
{{~ end ~}}

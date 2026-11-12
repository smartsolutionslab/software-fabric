# Test extra: accessibility

**Status:** Accepted (generated)

## Decision

Component tests check rendered screens with axe (`vitest-axe`): `expect(await axe(container)).toHaveNoViolations()`. jsdom cannot judge contrast or layout, so this catches structure (names, roles, labels), not everything; end-to-end axe checks with Playwright follow when screens exist to check.
{{~ if project["frontend.architecture"] == "spa" ~}}

Each module screen gets a generated axe check of its own, `<Name>Page.axe.test.*`, beside it; `src/screens.test.ts` fails while a screen has neither that nor an axe check in its own `<Name>Page.test.*`. The screen's own component test is left to the project: once it checks the screen with axe, the generated check skips itself, so a screen is checked once, and an upgrade never writes over a test the project wrote.
{{~ end ~}}

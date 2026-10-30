# Test extra: accessibility

**Status:** Accepted (generated)

## Decision

Component tests check rendered screens with axe (`vitest-axe`): `expect(await axe(container)).toHaveNoViolations()`. jsdom cannot judge contrast or layout, so this catches structure (names, roles, labels), not everything; end-to-end axe checks with Playwright follow when screens exist to check.

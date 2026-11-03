# Test discipline: red first

**Status:** Accepted (generated)

## Decision

New behaviour starts with a test that is run and seen failing before the code that makes it pass exists. The failing output is quoted in the pull request under `## Tests observed failing`; a CI check refuses a pull request that changes `src/` or `web/` without it.

A behaviour-preserving change (a refactor) quotes its covering tests passing before the change instead, under `## Tests observed passing before the change`; they must pass unmodified after it. The check accepts either section, each with its output in a code block. A refactor without covering tests is a rewrite: it starts by writing them, and a refactor that also fixes a bug is two changes, so the passing tests do not encode the bug.

## Consequences

- A test that was never red proves nothing about the code it claims to test; the quoted output is the evidence a reviewer can check.
- A dependency update by Dependabot quotes no failing test: a pull request whose author is `dependabot[bot]` and which changes dependency manifests and lockfiles only passes the check with a note (the board's `dependency-update.sh` decides it, see the board protocol's ADR). Any other pull request touching `src/` or `web/`, Dependabot's included, still needs the section.
- Agents split the work: the test writer writes and runs the tests and hands over the output; the engineer makes them pass without editing them.

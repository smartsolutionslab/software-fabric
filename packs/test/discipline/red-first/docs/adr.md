# Test discipline: red first

**Status:** Accepted (generated)

## Decision

New behaviour starts with a test that is run and seen failing before the code that makes it pass exists. The failing output is quoted in the pull request under `## Tests observed failing`; a CI check refuses a pull request that changes `src/` or `web/` without it.

A behaviour-preserving change (a refactor) quotes the covering tests passing before and after instead; a refactor without covering tests is a rewrite.

## Consequences

- A test that was never red proves nothing about the code it claims to test; the quoted output is the evidence a reviewer can check.
- Agents split the work: the test writer writes and runs the tests and hands over the output; the engineer makes them pass without editing them.

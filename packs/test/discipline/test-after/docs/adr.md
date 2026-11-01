# Test discipline: test after

**Status:** Accepted (generated)

## Decision

Tests may be written after the code they test. No pull request has to show its tests failing first; what is checked is the result: the coverage gate fails CI when a module's Domain or Application, or the Kernel, is below the line coverage `tests/coverage.props` sets (90 %, 80 % and 90 % as generated).

The gate itself comes with the test framework (see its ADR), so it is the same gate a red-first project has; this choice only drops the red-first evidence.

## Consequences

- Nothing proves a test was ever able to fail. A test that runs a line without checking its result still counts toward coverage, so reviewers read the assertions, not only the number.
- Infrastructure and Api code is not measured; the integration tests are what cover it.
- Moving to red-first later is a project decision with its own ADR; the gate stays as it is.

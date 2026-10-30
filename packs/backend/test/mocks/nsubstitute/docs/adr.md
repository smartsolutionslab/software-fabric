# Test doubles: NSubstitute

**Status:** Accepted (generated)

## Decision

Substitutes come from NSubstitute (`Substitute.For<IClock>()`), with NSubstitute.Analyzers turning misuse — substituting a non-virtual member, misplaced argument matchers — into build errors. Prefer a hand-written fake where it reads more clearly than a substitute.

## Consequences

- A substitute that would silently not intercept a call does not build.

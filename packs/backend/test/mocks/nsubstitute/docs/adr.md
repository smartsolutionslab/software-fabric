# Test doubles: NSubstitute

**Status:** Accepted (generated)

## Decision

Substitutes come from NSubstitute (`Substitute.For<IPaymentGateway>()`), with NSubstitute.Analyzers turning misuse — substituting a non-virtual member, misplaced argument matchers — into build errors. Prefer a hand-written fake where it reads more clearly than a substitute. The clock is never substituted: tests pass the base's `FakeTimeProvider` where code takes a `TimeProvider`.

## Consequences

- A substitute that would silently not intercept a call does not build.

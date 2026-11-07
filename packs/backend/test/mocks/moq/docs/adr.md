# Test doubles: Moq

**Status:** Accepted (generated)

## Decision

Mocks come from Moq (`new Mock<IPaymentGateway>(MockBehavior.Strict)`), with Moq.Analyzers turning misuse — mocking a
sealed class, setting up or verifying a member that cannot be overridden, a callback that does not match the method —
into build errors. Prefer a hand-written fake where it reads more clearly than a mock. The clock is never mocked: tests
pass the base's `FakeTimeProvider` where code takes a `TimeProvider`.

Moq is licensed under the BSD 3-Clause licence (the licence expression of the pinned version, 4.21.0, published
2026-09-24 on NuGet), and so is Moq.Analyzers (0.4.2). Moq 4.20.0 shipped SponsorLink, a closed-source analyzer that
read the developer's git e-mail address during the build and sent a hash of it to a remote service; 4.20.2 removed it,
and the versions from 4.20.0 to 4.20.2 are unlisted on NuGet. The pinned version was checked for it: the package holds
no analyzer and nothing named SponsorLink, and depends on nothing but Castle.Core (Apache-2.0) and
System.Threading.Tasks.Extensions. A version change rechecks the package's contents, not only its licence. Both versions
are pinned in `Directory.Packages.props`.

## Consequences

- A mock that would silently not intercept a call does not build.
- Moq's namespace is imported into every test project, so its `Range`, `Match` and `Capture` sit beside the BCL's; a
  test that needs the BCL's type names it in full.

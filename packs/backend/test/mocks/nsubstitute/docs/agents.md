## Test doubles: NSubstitute

- Substitute interfaces, not classes: `var payments = Substitute.For<IPaymentGateway>(); payments.ChargeAsync(...).Returns(...)`.
- Substitute only what crosses the module's boundary (external services); use real domain objects.
- Never substitute the clock: pass a `FakeTimeProvider` (`Microsoft.Extensions.Time.Testing`) where code takes a `TimeProvider`, and move it with `Advance(...)` or `SetUtcNow(...)`.

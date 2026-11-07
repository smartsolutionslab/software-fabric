## Test doubles: Moq

- Mock interfaces, not classes: `var payments = new Mock<IPaymentGateway>(MockBehavior.Strict); payments.Setup(p => p.ChargeAsync(...)).ReturnsAsync(...)`, and pass `payments.Object`.
- Mock only what crosses the module's boundary (external services); use real domain objects.
- Never mock the clock: pass a `FakeTimeProvider` (`Microsoft.Extensions.Time.Testing`) where code takes a `TimeProvider`, and move it with `Advance(...)` or `SetUtcNow(...)`.

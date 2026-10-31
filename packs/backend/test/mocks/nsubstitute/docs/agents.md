## Test doubles: NSubstitute

- Substitute interfaces, not classes: `var clock = Substitute.For<IClock>(); clock.Now.Returns(...)`.
- Substitute only what crosses the module's boundary (clock, external services); use real domain objects.

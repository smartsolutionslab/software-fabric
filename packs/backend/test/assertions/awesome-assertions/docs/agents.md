## Assertions: AwesomeAssertions

- Assert with AwesomeAssertions: `result.Should().Be(expected)`, `items.Should().Contain(x)`, `FluentActions.Invoking(() => ...).Should().Throw<T>()`. The framework's `Assert` does not compile in tests.
- Give a reason with `because:` (`total.Should().Be(42, because: "...")`); naming the argument keeps it from being taken as another expected item.
- Need the asserted value (the single item, the exception)? End with `.Value()`, not `.Which`, so the line also stands as a statement.

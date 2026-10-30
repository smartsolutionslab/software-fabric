# Argument guards: Ensure.That

**Status:** Accepted (generated)

## Decision

Every argument precondition is a fluent chain on `Ensure.That(value)` from `src/Kernel`: `Ensure.That(services).IsNotNull();`, `Ensure.That(name).IsNotNullOrWhiteSpace().HasMaxLength(200);`, `Ensure.That(amount).AtLeast(0m);`, `Ensure.That(id).IsNotEmpty();`. The parameter name comes from the call site. The BCL helpers (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`, ...) are banned by the Banned API analyzer in every project but the AppHost, which references no kernel.

Value objects validate their invariants with the same chains in their factories.

This is the convention of Smart Sentinel Eye.

## Consequences

- One way to write a guard; chains read as the rule they enforce.
- A guard that the chain cannot express goes into `Satisfies(predicate, message)` or a new method on the chain, not a bare `throw`.

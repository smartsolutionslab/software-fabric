# Argument guards: Ensure.That

**Status:** Accepted (generated)

## Decision

Every argument precondition is a fluent chain on `Ensure.That(value)` from `src/Kernel`: `Ensure.That(services).IsNotNull();`, `Ensure.That(name).IsNotNullOrWhiteSpace().HasMaxLength(200);`, `Ensure.That(amount).AtLeast(0m);`, `Ensure.That(id).IsNotEmpty();`. The parameter name comes from the call site. The BCL helpers (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`, ...) are banned by the Banned API analyzer in every project but the AppHost, which references no kernel.

Value objects validate their invariants with the same chains in their factories.

Every failed guard, a null check included, throws `GuardException` (`src/Kernel`), an `ArgumentException` that carries the parameter name. Its own type tells a failed precondition apart from an `ArgumentException` a bug throws.

This is the convention of Smart Sentinel Eye.

## Consequences

- One way to write a guard; chains read as the rule they enforce.
- A guard that the chain cannot express goes into `Satisfies(predicate, message)` or a new method on the chain, not a bare `throw`.
- A ban nobody reads fails silently, so an architecture rule evaluates every project with MSBuild and fails naming any project but the AppHost that does not read `build/guards/BannedSymbols.txt`. The list keeps that exact name: the analyzer finds ban lists by it (roslyn-analyzers#5622).

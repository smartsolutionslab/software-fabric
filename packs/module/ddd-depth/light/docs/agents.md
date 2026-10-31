## DDD depth: light

- Introduce a value object when a value has its own rules or meaning (`Money`, `EmailAddress`, `InvoiceNumber`); a plain `string` or `int` is fine for a value without rules.
- Aggregate roots never expose public setters; state changes go through methods. An architecture rule checks this.

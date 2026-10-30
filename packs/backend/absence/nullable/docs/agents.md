## Absence: nullable references

- A method that may find nothing returns `T?`; callers check with `is null` / pattern matching before use. Never silence the compiler with `!` without a comment saying why it cannot be null.

# Absence: nullable references

**Status:** Accepted (generated)

## Decision

A value that may be absent is a nullable reference (`Invoice?`). Nullable reference types are on everywhere and warnings are errors, so the compiler makes callers check before use. No `Option<T>`.

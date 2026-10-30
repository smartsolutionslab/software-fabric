# Realtime: none

**Status:** Accepted (generated)

## Decision

The frontend has no push channel. Screens fetch, and RTK Query refetches on focus or by polling where freshness matters (`pollingInterval`).

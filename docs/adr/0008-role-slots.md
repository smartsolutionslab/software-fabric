# ADR-0008: Role slots

**Status:** Accepted
**Date:** 2026-10-31

## Context

A slot belongs to the pack that owns the file it fills (ADR-0002). The web
app's `package.json`, entry point and test setup belong to the framework's
pack, so React's pack declares `web.dependencies`, `web.providers`, `web.env`
and ten more, and RTK Query's pack the request-header slots. Packs that are
shared across frameworks (zod, tanstack-table, echarts, oidc-client-ts,
accessibility) contribute to them. With Vue or Orval chosen, those slots do
not exist and the composition fails: the shared packs' contract was a property
of one option, not of the dimension.

## Decision

**A dimension names in the catalog the slots every one of its options must
declare**, its role slots, each with a description and separator:

```yaml
frontend.framework:
  role-slots:
    web.dependencies: { description: "...", separator: ",\n" }
```

- The pack loader rejects an option pack that does not declare a role slot of
  its dimension, or declares it with another separator, naming the slot.
- The file stays with the option's pack; only the slot's name and joining are
  the dimension's. A shared pack contributes as before, and whatever its
  contribution must look like for a given option (a Vue provider differs from a
  React one) it decides at generation time (ADR-0006).
- `frontend.framework` has 13 role slots, `frontend.api-client` two
  (`web.api-imports`, `web.api-headers`). Slots only one partner uses (the
  Redux store's, filled by RTK Query) stay ordinary.

## Consequences

- A new framework or API client learns its contract from the catalog and fails
  fast, at load, when it misses part of it.
- Changing a role slot is a catalog change and touches every option of the
  dimension, which is the point.

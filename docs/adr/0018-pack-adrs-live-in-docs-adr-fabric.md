# ADR-0018: Pack ADRs live in `docs/adr/fabric/`, named after their option

**Status:** Accepted (owner, 2026-11-20, #840: option (b) of three).
**Date:** 2026-11-20

## Context

Every selected pack's ADR is written into the generated project (ADR-0002).
Until #840 the generator named it `docs/adr/NNNN-<dimension>-<option>.md`,
numbered by the pack's place among the selected packs, base first. A
generated project is meant to keep writing its own ADRs into `docs/adr/`, and
the pilot has done so since fabric-pilot#33. Its trial upgrade from 0.7.0
(fabric-pilot#85) showed two defects of that numbering:

- **A new pack renumbers the existing ones.** Adding `frontend.font.inter`
  and `backend.data-access.reads.ef-core` moved
  `0051-backend-data-access-reads-ef-core.md` to `0053-…`, and every
  reference to the old number went stale.
- **A new pack takes numbers the project already uses.** The pilot's own
  `0052-identity-staff-write-for-a-named-tenant.md` and
  `0053-identity-staff-reader-service-account.md` would sit beside the
  generator's new `0052-frontend-font-inter.md` and
  `0053-backend-data-access-reads-ef-core.md`.

A pack ADR's name must therefore not depend on the other choices or on the
generator version, and the generator must not claim numbers that belong to the
project. No ADR settled the name; `docs/packs.md` and `docs/slots.md` only
described the numbering.

## Options weighed

All three keep the upgrade itself unchanged (ADR-0016 E4, D8): base, the old
generator's tree, has the numbered name and theirs the new one, so the
three-way merge removes the old file and adds the new. A pack ADR the project
never edited simply moves; one it edited is a conflict at the old name ("changed
by the project, deleted by" the new fabric); the project's own ADRs are in
neither tree and are never touched. Generated files cite pack ADRs only as
"(docs/adr)", never by number, and pack ADRs cite the generator's ADRs, not each
other, so nothing generated goes stale under any of them.

- **(a) Unnumbered, in `docs/adr/`:** `docs/adr/<dimension>-<option>.md`.
  Stable, and cannot collide with the project's numbered ADRs, but the
  generator still writes into the folder the project numbers, and ADR tools
  that expect `NNNN-` there see unnumbered files.
- **(b) A folder of its own, unnumbered:**
  `docs/adr/fabric/<dimension>-<option>.md`. As stable as (a), and
  `docs/adr/` itself belongs to the project alone.
- **(c) A reserved number range**, a permanent number per option kept in the
  catalog (e.g. 9000–9999). Keeps the `NNNN-` format, but needs a hand-kept
  registry with a lint of its own, and still claims numbers the project might
  use.

Set aside: freezing the selection-order numbers per project in `fabric.yaml`,
new packs taking the next free number. The generator would have to read the
project's own `docs/adr/` to find that number, so base and theirs would no
longer be functions of `fabric.yaml` alone (ADR-0016 D1, AGENTS.md "same input,
same output").

## Decision

**(b)** (owner, 2026-11-20). The generator renders each selected pack's ADR to
`docs/adr/fabric/<dimension>-<option>.md`: the option's id with its dots turned
into hyphens (`backend.tenancy.column` → `backend-tenancy-column.md`), no
number. The name changes only when the option is renamed, which ADR-0016 D7
already makes a major change. The generator writes nothing else under
`docs/adr/`; that folder's numbered ADRs are the project's own.

`fabric upgrade` from a project with numbered pack ADRs moves them through the
ordinary merge, and its summary lists every move, `old -> new`, under
"pack ADRs moved under docs/adr/fabric/". What the merge cannot do is named
under ADR-0016 D1's "not guaranteed":

- the project's own ADRs, docs and code that cite a pack ADR by its old file
  name or number (`ADR-0018`) are not rewritten: the person or agent points
  them at the new name;
- a pack ADR the project edited is left as a conflict at its old name, and the
  summary names where its edits go: carry them to the new name, then
  `git rm` the old file.

## Consequences

- A pack added, removed or reordered changes no other pack ADR's name, and no
  pack ADR can take a number the project uses (#840's upgrade test).
- Every generated profile's pack ADRs move once, from `docs/adr/NNNN-…` to
  `docs/adr/fabric/…`. The contents are unchanged.
- Pack ADRs no longer show the order the packs were selected in; the order of
  the sections in `AGENTS.md` still does.
- The base's `AGENTS.md` says where each kind of ADR lives.

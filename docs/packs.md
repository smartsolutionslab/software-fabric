# Writing a pack

A pack implements one catalog option (ADR-0002). This page is the contract
the generator enforces; `tests/fixtures/minimal/packs` is a working
example of every rule on it.

## Where a pack lives

`packs/<dimension path>/<option>/pack.yaml`. The path *is* the option:
`packs/frontend/ui-library/shadcn/` holds `frontend.ui-library.shadcn`, and
`pack.yaml` must say `option: frontend.ui-library.shadcn`.

Packs under `packs/base/` are the **base**: always part of every project,
no catalog entry. The base declares the slots the other packs fill.

## What a pack declares

`schema/pack.schema.json` is the authority. In short:

| Field | Meaning |
|---|---|
| `files` | Files the pack owns. Required — write `files: []` for a pack that only fills slots. |
| `slots` | Contributions to slots other packs declare. |
| `declares-slots` | Slots this pack opens, with `cardinality`. |
| `agents` | `agents-md` (section for the project's AGENTS.md) and `briefs` per role. |
| `adr` | The decision record the project receives. |
| `gates` | At least one check that fails the project's build when the option's rule is broken. |

Every path is relative to the pack directory and must stay inside it.

## Targets

A file's `target` is its path in the generated project, with `/`.

- `per-module: true` renders the file once per module, and its target
  **must** contain `{{ module.name }}`. A module option's per-module file
  renders for the modules that chose it; any other pack's for every module.
- A project file's target must **not** contain `{{ module.name }}`.
- Two packs claiming the same target is an error. So is a pack claiming
  `fabric.yaml`, which the generator writes.

## What a template sees

Templates are [Scriban](https://github.com/scriban/scriban).

| Name | Content |
|---|---|
| `project["<dimension>"]` | The project's choice: a string, or a list for multi-valued dimensions |
| `module_defaults["<dimension>"]` | What new modules start from |
| `modules` | List of `{ name, values }` |
| `module` | `{ name, values }` — per-module files only |
| `slots["<slot>"]` | The slot's contributions in order, joined by newlines — files only, not slot contributions |

Unknown variables are errors. `date`, `timespan` and `math` are not
available: the same choices must always render the same bytes. Output
line endings are LF.

## Slots

Contributions are ordered by `order`, then by option id. A slot declared
twice, a contribution to a slot no selected pack declares, and a
cardinality breach are errors.

## Adding a module later

`fabric add-module` only creates files. If a module chooses an option
whose pack the project does not use yet, and that pack has project files
or slot contributions, add-module refuses: that content would have to be
merged into files that may have changed. Keep project-wide content of
module options small, or expect it to be chosen at generation time.

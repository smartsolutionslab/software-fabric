# Writing a pack

A pack implements one catalog option (ADR-0002). This page is the contract
the generator enforces; `tests/fixtures/minimal/packs` is a working
example of every rule on it.

## Where a pack lives

`packs/<dimension path>/<option>/pack.yaml`. The path *is* the option:
`packs/frontend/ui-library/shadcn/` holds `frontend.ui-library.shadcn`, and
`pack.yaml` must say `option: frontend.ui-library.shadcn`.

Until its pack exists an option is `tier: experimental` in the catalog. The
pack's PR moves it to `supported`; a test fails the build when a `supported`
option has no pack (ADR-0003).

A new pack puts its option into the pairwise matrix (ADR-0003), so the PR
that adds it also regenerates `build/matrix.json`; a test fails while the
file is stale:

```bash
dotnet run --project src/Fabric.Cli -- matrix --catalog catalog/catalog.yaml --packs packs \
  --extra build/matrix-extra.yaml --output build/matrix.json
```

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
| `modules` | Modules the pack brings, each with pinned module choices (below). |

Every path is relative to the pack directory and must stay inside it.

## Targets

A file's `target` is its path in the generated project, with `/`.

- `per-module: true` renders the file once per module, and its target
  **must** contain `{{ module.name }}` (`order-handling`) or
  `{{ module.namespace }}` (`OrderHandling`). A module option's per-module file
  renders for the modules that chose it; any other pack's for every module.
- A project file's target must **not** contain either.
- Two packs claiming the same target is an error. So is a pack claiming
  `fabric.yaml`, which the generator writes.

## Conditional files

A file entry may carry `when:`, a Scriban condition evaluated against the
same names its template sees (below), `module` included for per-module files:

```yaml
files:
  - { template: templates/FakeChatClient.cs.sbn, target: tests/Fakes/FakeChatClient.cs, when: 'project["backend.llm"] != "none"' }
```

- `false`: the file is not written. `true`: it renders as any other file.
- Anything else is a generation error naming the pack, the template and the
  target: a condition that does not parse, reads an unknown name or a key the
  model lacks (`project["backend.databse"]`), or yields something other than
  `true` or `false` (`project["backend.llm"]` alone is a string). It is never
  taken as `false` (ADR-0006 rule 2).
- A dimension inactive for the project is absent from `project`, so reading
  it is an error too; test for it first with
  `object.has_key(project, "<dimension>") && ...`.
- A template that renders empty is still written, with or without `when`;
  only the condition decides whether a file exists.
- The target is claimed whatever the condition says, so two packs may not
  share a target by giving it opposite conditions.
- Template functions from `templates.functions` are not available in a
  condition; it reads the model, nothing else.

## Modules a pack brings

A base or project-scoped pack may bring modules (ADR-0005). Each key is a
module name, each value the module-scoped choices it pins:

```yaml
modules:
  dashboards: { module.style: crud, module.cqrs: logical }
```

- `fabric new` adds every module the selected packs bring, in name order, as
  `fabric add-module <name> --set ...` would: the pins first, the project's
  module defaults for the rest. They are recorded in `fabric.yaml` like any
  module, so the module packs give each one the normal skeleton.
- The declaring pack adds the module's own code as per-module files that
  render for it only: `when: 'module.name == "dashboards"'`.
- A pin the catalog does not allow (an unknown option, a project-scoped
  dimension, an inactive one, a broken `requires` or `conflicts`) is a
  generation error naming the pack. A project option's condition on a
  module dimension is still checked against the module defaults, as
  `fabric validate` and the catalog lint check it (they see no packs), so a
  brought module cannot satisfy it. Two packs may bring the same module; a
  dimension they pin differently (as a set, for a multi-select dimension) is
  an error naming both.
- `fabric add-module` refuses a name a selected pack brings.
- A module-scoped pack may not bring a module: the modules decide which
  module-scoped packs are selected, so such a pack would decide its own
  selection. The loader rejects it.

## What a template sees

Templates are [Scriban](https://github.com/scriban/scriban).

| Name | Content |
|---|---|
| `name` | The project's name, lower kebab-case (`smart-shop`) |
| `namespace` | The same in PascalCase (`SmartShop`), for C# namespaces |
| `project["<dimension>"]` | The project's choice: a string, or a list for multi-valued dimensions |
| `module_defaults["<dimension>"]` | What new modules start from |
| `settings["<dimension>.<option>.<setting>"]` | A chosen option's setting, a number: the value `fabric.yaml` records, the declared default unless set (`settings["backend.llm.extras.evals.threshold"]` is `4`). Only chosen options' settings are present (ADR-0014) |
| `modules` | List of `{ name, namespace, values }` |
| `module` | `{ name, namespace, values }` — per-module files only |
| `slots["<slot>"]` | The slot's contributions in order, joined by its separator — files only, not slot contributions |

A template may branch on another dimension (`{{ if project["backend.database"] == "postgres" }}`) when its own output differs by that choice; see ADR-0006 for when that is right and when a slot is.

Unknown variables are errors. `date`, `timespan` and `math` are not
available: the same choices must always render the same bytes. Output
line endings are LF; whitespace-only lines become empty and runs of blank
lines collapse to one, so an empty slot leaves no gap.

## Slots

Contributions are ordered by `order`, then by option id, and joined by
the slot's `separator` (a newline unless declared — JSON lists declare
`",\n"`). A slot declared twice, a contribution to a slot no selected
pack declares, and a cardinality breach are errors.

A contribution may carry `optional: true` when the slot belongs to a
choice the pack may be combined with but does not require — a converter
for an event store that only some projects have:

```yaml
slots:
  - { slot: event-store.json-converters, template: templates/converter.sbn, optional: true }
```

If no selected pack declares the slot, the contribution is dropped and
generation goes on; if one does, it counts towards the slot's cardinality
and renders like any other. Without `optional` (or with `false`) an
undeclared slot stays an error naming the pack and the slot, so a
misspelled slot name is still caught everywhere it is not marked
optional. Mark a contribution optional only when some valid combination
of choices has no pack declaring its slot.

A generated web app must pass its own `pnpm format:check` (Prettier), so a
contribution to a file under `web/` is written as Prettier prints it at
the depth where it lands; the template adds the trailing comma after the
last entry of a list.

### Role slots

A pack shared across the options of another dimension (zod, an auth client)
contributes to slots that dimension's chosen option declares. So that it finds
them whichever option is chosen, the catalog names them as **role slots** of
that dimension (ADR-0008):

```yaml
frontend.framework:
  role-slots:
    web.dependencies: { description: "Entries of the web app's dependencies.", separator: ",\n" }
```

Every pack of an option of `frontend.framework` must declare each role slot,
joined by the catalog's separator; the pack loader rejects one that does not,
naming the slot. A pack may describe the slot more concretely in its own
declaration.

## Template functions

A pattern that runs through every pack is a function, not a branch. Packs contribute Scriban code to the reserved slot
`templates.functions`; the generator prefixes it to every template and slot contribution instead of rendering it.
Write `{{ not_null "services" }}`, never a guard of your own: the chosen guards pack decides whether that renders
`Ensure.That(services).IsNotNull();` or `ArgumentNullException.ThrowIfNull(services);`.

| Function | Defined by | Renders |
|---|---|---|
| `not_null name` | `backend.guards.*` | a null guard for the argument `name` |
| `not_blank name` | `backend.guards.*` | a null-or-whitespace guard for the string argument `name` |

A contribution is plain Scriban code (`func ... end`), no `{{ }}`.

## What the generator writes from every pack

- **ADR:** each selected pack's `adr` is rendered to
  `docs/adr/NNNN-<dimension>-<option>.md`, numbered in selection order
  (base first).
- **AGENTS.md:** each selected pack's `agents-md` is a contribution to
  `agents.sections`, in selection order.
- **Briefs:** each brief a pack gives for a role is a contribution to
  `agents.brief.<role>`.

Those slots are reserved: the base declares them, the generator fills
them, and a pack contributing to them directly is an error. All three are
templates and see the same model as any other.

The full slot set for a generated project is in `docs/slots.md`.

## Adding a module later

`fabric add-module` only creates files. If a module chooses an option
whose pack the project does not use yet, and that pack has project files
or slot contributions, add-module refuses: that content would have to be
merged into files that may have changed. Keep project-wide content of
module options small, or expect it to be chosen at generation time.

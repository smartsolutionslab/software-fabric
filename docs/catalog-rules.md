# Catalog rules

`catalog/catalog.yaml` holds dimensions, options and rules. This page
defines what the rule fields mean, so the generator and a human reading the
catalog agree.

[`catalog.md`](catalog.md) lists every dimension and option as they stand,
generated from the catalog. A change to the catalog regenerates it in the
same pull request; a test fails while it is stale:

```bash
dotnet run --project src/Fabric.Cli -- reference --catalog catalog/catalog.yaml --output docs/catalog.md
```

## Scope

| `scope` | Meaning |
|---|---|
| `project` | One value per generated repository. |
| `module` | One value per backend module (bounded context). Frontend dimensions are always `project` in V1; a micro-frontend follows its module. |

## Dimensions

| Field | Meaning |
|---|---|
| `scope` | See above. |
| `multi: true` | Several options may be chosen at once (a set, not a single value). |
| `active-when` | The dimension exists only when this condition holds. When it does not, the dimension **must not** be set; when it does, it **must** be set (or take its `default`). |
| `default` | Value used when the dimension is active and not chosen. |
| `default-from` | Another single-valued dimension whose value is the default when it is also one of this dimension's options (`default-from: backend.llm`: the embeddings provider is the LLM provider when that provider has embeddings). When it is not, the dimension has no default, and an active one must be set; the error names the followed dimension and its value. Only on a single-valued dimension, never beside `default`, and the followed dimension must share at least one option with it (ADR-0012). |
| `requires` | List of conditions, **all** of which must hold whenever the dimension is set, whichever of its options are chosen, an empty set included. A module dimension's are checked per module, so `fabric add-module` refuses a module that breaks them and names it. For a rule no option can carry because it must also fire when none is chosen (`module.communication: requires: ["module.communication has integration-events"]`). It may name its own dimension only when that is multi-valued, with `has`; a single-valued dimension that refuses one of its own values should not offer it. An inactive dimension is not checked. The rejection names the dimension, not an option: `module.communication requires module.communication has integration-events, but module.communication is empty.` |
| `options` | Map of option id → option. |

## Options

| Field | Meaning |
|---|---|
| `tier` | `supported` (its pairs pass in the matrix) or `experimental` (generated with a warning). An option without a pack under `packs/` is `experimental`. The PR that adds its pack puts it into the matrix but leaves it `experimental`; it is promoted to `supported` once every matrix combination containing it is green in the same nightly run (ADR-0003, amended 2026-11-06). `SupportTierTests` fails the build when a `supported` option has no pack, or is in neither a profile nor a combination of `build/matrix.json`. |
| `requires` | List of conditions, **all** of which must hold when this option is chosen. |
| `conflicts` | List of conditions, **none** of which may hold when this option is chosen. |
| `note` | Human text. Not interpreted. |
| `settings` | Numbers the option needs besides being chosen, by id: each has `default`, `minimum` and `maximum` (and an optional `note`). Only on a project-scoped dimension; the id is lower-case letters, digits and hyphens; `minimum <= default <= maximum` (ADR-0014). |

## Settings

A chosen option's settings are recorded in `fabric.yaml` under `settings`,
keyed `<dimension>.<option>.<setting>`, after `project`:

```yaml
settings:
  backend.llm.extras.evals.threshold: 4
```

- The map appears only when a chosen option declares a setting, so a project
  without one has no `settings` line.
- `fabric new --set <dimension>.<option>.<setting>=<number>` sets one; a
  setting not set takes its `default`. A value outside its range, one that is
  not a number, one the option does not declare, or one for an option that is
  not chosen is an error naming the key.
- A setting is a number, not a choice: the generated code may read it (the
  evals job reads its threshold from `fabric.yaml` at run time), and a project
  may change it there by hand. `fabric add-module` keeps the map and checks each
  value against the catalog again; `--set` of a setting there is refused, since
  it belongs to the project.

## Conditions

A condition is a single string:

```
<dimension> == <option>
<dimension> != <option>
<dimension> in (<option>, <option>, …)
<dimension> has <option>        # for multi dimensions
```

- `has` is only for multi-valued dimensions; `==`, `!=` and `in` only for
  single-valued ones. The catalog self-check rejects any other pairing.
- A condition may not refer to its own dimension in `active-when`, nor in a
  single-valued dimension's `requires`.
- A dimension that is **inactive** never satisfies `==`, `in` or `has`, and
  always satisfies `!=`.
- Module-scoped conditions are evaluated **per module**: a module option's
  rules and a module dimension's `requires` against that module's values and
  the project's. A project-scoped option or dimension that refers to a module
  dimension holds if it holds for **any** module.
- No `or`, no nesting. If a rule needs more, it is two options or a
  different dimension cut — keeping the grammar this small is deliberate,
  so the generator can explain every rejection in one sentence.

## Profiles

A profile is a partial choice. `extends` copies another profile first.
Anything a profile does not set takes the dimension's `default` (or its
`default-from`); a dimension that is active, has no default and is not set
is an error. A value set where its dimension does not apply is reported on
its own: the dimensions it would switch on report nothing missing.

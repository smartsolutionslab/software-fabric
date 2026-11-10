# Writing a pack

A pack implements one catalog option (ADR-0002). This page is the contract
the generator enforces. Each rule names the test that enforces it, or the CI
script where no test can; `PackGuideTests` fails when a section names neither,
or names a test or script that does not exist.

`tests/fixtures/minimal` is a small working catalog and pack set: the
generator's own tests run on it (`GeneratorTests`, `AddModuleTests`), and
`ConditionalFileTests` runs on a copy of it with one file entry added. The
rules it has no example of (role slots, brought modules, optional
contributions, template functions) are shown working by the tests named
under each, on packs those tests write themselves.

## Where a pack lives

`packs/<dimension path>/<option>/pack.yaml`. The path *is* the option:
`packs/frontend/ui-library/shadcn/` holds `frontend.ui-library.shadcn`, and
`pack.yaml` must say `option: frontend.ui-library.shadcn`
(`PackLoaderTests.Rejects_an_option_that_does_not_match_its_directory`). The
option must be in the catalog
(`PackLoaderTests.Rejects_a_pack_for_an_option_not_in_the_catalog`).

Packs under `packs/base/` are the **base**: always part of every project,
no catalog entry (`PackLoaderTests.A_base_pack_needs_no_catalog_option`,
`ComposerTests.Selects_the_base_packs_and_one_pack_per_chosen_option`). The
base declares the slots the other packs fill.

An option is `tier: experimental` until its pairs pass (ADR-0003 and its
amendment of 2026-11-06). A `supported` option needs a pack
(`SupportTierTests.Every_supported_option_of_the_repository_catalog_has_a_pack`)
and must be in a profile or `build/matrix.json`
(`SupportTierTests.Every_supported_option_of_the_repository_catalog_is_in_a_profile_or_the_matrix`).
Adding a pack does not promote its option: the PR that changes the tier
later cites the nightly run in which every matrix combination containing
the option was green. That PR is a catalog change, so it regenerates
`docs/catalog.md` with `fabric reference` (#383, `docs/catalog-rules.md`);
`ReferenceCommandTests.The_checked_in_reference_is_what_the_catalog_gives_today`
fails while the file is stale.

## The matrix

The pairwise matrix's domain is every option with a pack (ADR-0003
amendment), so the PR that adds a pack puts its option into the matrix and
regenerates `build/matrix.json` in the same change; any change under
`packs/` or to the catalog can change it too.
`MatrixCommandTests.The_checked_in_matrix_is_what_the_catalog_the_packs_and_the_extras_give_today`
fails while the file is stale:

```bash
dotnet run --project src/Fabric.Cli -- matrix --catalog catalog/catalog.yaml --packs packs \
  --extra build/matrix-extra.yaml --output build/matrix.json
```

- Every pull request generates every combination of the file
  (`build/matrix-generate.sh`, the `matrix-generation` job of
  `.github/workflows/ci.yml`); a combination that fails to generate fails
  the PR. The nightly run (`.github/workflows/matrix.yml`) builds each one
  and runs its unit, architecture and integration tests and its frontend's
  build, lint and tests, without end-to-end tests.
- A combination is a full choice set without a profile. Its `choices` are
  what `fabric new --choices <file>` takes: a JSON object of dimension to an
  option, or to a list of options for a multi-valued dimension. Dimensions
  it leaves out take the catalog's defaults, `--set` overrides apply on top
  of it, and it takes no `--profile`
  (`NewCommandTests.A_choice_set_generates_the_project_its_profile_would_and_records_no_profile`,
  `NewCommandTests.An_override_wins_over_the_choice_set`,
  `NewCommandTests.A_choice_set_with_a_profile_is_refused_before_anything_is_written`).
  Every combination generates that way
  (`MatrixCommandTests.Each_combination_generates_with_fabric_new_from_its_choices`).
- To reproduce a failing combination, run it by name, as CI does:
  `build/profile-ci.sh --combination pairwise-2 --steps generate,build,test,web`.
- A defect that needs three options together, or a second module style, is
  covered by adding that combination, with its reason, to
  `build/matrix-extra.yaml`; extra combinations come first
  (`MatrixCommandTests.Puts_the_extra_combinations_first`), and one no valid
  choice set contains fails the matrix
  (`MatrixCommandTests.An_extra_combination_that_cannot_be_built_fails_and_writes_nothing`).

## What a pack declares

`schema/pack.schema.json` is the authority; a field it does not know is an
error (`PackLoaderTests.Rejects_an_unknown_field`). In short:

| Field | Meaning |
|---|---|
| `files` | Files the pack owns. Required (`PackLoaderTests.Rejects_a_pack_without_files`); write `files: []` for a pack that only fills slots (`PackLoaderTests.A_pack_that_only_fills_slots_writes_files_as_an_empty_list`). |
| `slots` | Contributions to slots other packs declare. |
| `declares-slots` | Slots this pack opens, with `cardinality` and `separator` (`PackLoaderTests.Reads_declared_slots_with_their_cardinality`). |
| `agents` | `agents-md` (section for the project's AGENTS.md) and `briefs` per role. |
| `adr` | The decision record the project receives. Required (`PackLoaderTests.Rejects_a_pack_without_an_adr`). |
| `gates` | At least one check that fails the project's build when the option's rule is broken (`PackLoaderTests.Rejects_a_pack_without_a_gate`), of a known kind (`PackLoaderTests.Rejects_a_gate_of_unknown_kind`). |
| `modules` | Modules the pack brings, each with pinned module choices (below). |

Every path is relative to the pack directory, must stay inside it
(`PackLoaderTests.Rejects_a_path_that_leaves_the_pack_directory`) and must
exist (`PackLoaderTests.Rejects_a_referenced_file_that_does_not_exist`).

## Targets

A file's `target` is its path in the generated project, with `/`.

- `per-module: true` renders the file once per module, and its target
  **must** contain `{{ module.name }}` (`order-handling`) or
  `{{ module.namespace }}` (`OrderHandling`)
  (`ComposerTests.A_per_module_target_without_the_module_name_is_an_error`,
  `ComposerTests.A_per_module_target_may_use_the_module_namespace`). A module
  option's per-module file renders for the modules that chose it
  (`ComposerTests.A_per_module_file_of_a_module_option_renders_only_for_modules_that_chose_it`);
  any other pack's for every module
  (`ComposerTests.A_per_module_file_of_a_project_option_renders_for_every_module`).
- A project file's target must **not** contain either
  (`ComposerTests.A_project_file_using_the_module_name_is_an_error`,
  `ComposerTests.A_project_file_using_the_module_namespace_is_an_error`).
- **One template per target.** A target is claimed once: two packs claiming
  it is an error (`ComposerTests.Two_packs_claiming_the_same_file_is_an_error`),
  and so is one pack giving two of its file entries the same target, whatever
  their conditions
  (`ComposerTests.One_pack_giving_two_templates_the_same_target_is_an_error_whatever_their_conditions`).
  A file whose content differs by another choice (a React and a Vue
  `App.tsx`) is one template that branches inside (below); variants with
  different targets (`.tsx` and `.vue`) are separate entries with `when`.
- A pack may not claim `fabric.yaml`
  (`GeneratorTests.A_pack_may_not_write_fabric_yaml`): the generator writes
  it, with the choices, the generator's version and the catalog's hash (#381,
  `FabricFileTests.Records_the_generator_version_as_semver_without_build_metadata`).

## Conditional files

A file entry may carry `when:`, a Scriban condition evaluated against the
same names its template sees (below), `module` included for per-module files
(`ConditionalFileTests.A_per_module_condition_sees_the_module`):

```yaml
files:
  - { template: templates/FakeChatClient.cs.sbn, target: tests/Fakes/FakeChatClient.cs, when: 'project["backend.llm"] != "none"' }
```

- `false`: the file is not written
  (`ConditionalFileTests.A_file_whose_condition_is_false_is_not_written`).
  `true`: it renders as any other file
  (`ConditionalFileTests.A_file_whose_condition_is_true_is_written_as_rendered`).
- Anything else is a generation error naming the pack, the template and the
  target, never taken as `false` (ADR-0006): a condition that does not parse
  (`ConditionalFileTests.A_condition_that_does_not_parse_is_an_error_naming_the_pack_and_the_file`),
  reads an unknown name
  (`ConditionalFileTests.A_condition_reading_an_unknown_variable_is_an_error_naming_the_pack_and_the_file`)
  or a key the model lacks, such as `project["backend.databse"]`
  (`ConditionalFileTests.A_condition_reading_a_key_the_model_lacks_is_an_error_naming_the_pack_and_the_file`),
  or yields something other than `true` or `false`: `project["backend.llm"]`
  alone is a string
  (`ConditionalFileTests.A_condition_that_is_not_a_boolean_is_an_error_naming_the_pack_and_the_file`).
- A dimension inactive for the project is absent from `project`, so reading
  it is the same error; test for it first with
  `object.has_key(project, "<dimension>") && ...`.
- A template that renders empty is still written, with or without `when`;
  only the condition decides whether a file exists
  (`ConditionalFileTests.A_file_whose_condition_is_true_and_renders_empty_is_still_written`).
- The target is claimed whatever the condition says, so two packs may not
  share a target by giving it opposite conditions
  (`ComposerTests.Two_packs_may_not_share_a_target_by_giving_it_opposite_conditions`).
- The template functions (below) are not available in a condition; it reads
  the model, nothing else
  (`RendererTests.A_file_condition_cannot_call_the_contributed_functions`).

## Branching on another choice

A template may read another dimension
(`{{ if project["backend.database"] == "postgres" }}`) when its own output
differs by that choice (ADR-0006). Prefer a slot when the other pack can
declare one for all its options; a branch is for output that belongs to
this pack.

- The branch happens at generation time; the generated code never branches
  on a choice (ADR-0001).
- Every branch renders something valid for each option of the dimension it
  reads, or fails loudly; it never silently renders nothing. Failing loudly
  is an `#error` (or, in a web file, `throw new Error(...)`) that names the
  choice the pack does not handle yet and ADR-0006, so the generated
  project's build stops on it. Where the catalog can say so, a rule refuses
  the combination before anything is generated instead (M3-05,
  `docs/catalog-rules.md`).
- The vocabularies below are the exception: no pack may refuse another test
  framework, assertion library, integration option or identity provider,
  with `#error` or otherwise
  (`TestVocabularyTests.No_pack_refuses_its_tests_to_an_assertion_library_other_than_shouldly`,
  `IntegrationVocabularyTests.No_pack_refuses_its_tests_to_an_integration_option_or_test_framework_other_than_aspire_fixture_and_xunit`,
  `IdentityVocabularyTests.No_pack_refuses_another_identity_provider_or_auth_client`).
- The matrix covers the branches: the pack's option meets each option of
  the dimension it reads in some combination, every PR generates them all
  (`build/matrix-generate.sh`), and the nightly run builds and tests them
  (`.github/workflows/matrix.yml`).

## Modules a pack brings

A base or project-scoped pack may bring modules (ADR-0005). Each key is a
module name, each value the module-scoped choices it pins, an option or a
list for a multi-select dimension
(`PackModuleTests.A_pin_may_be_written_as_a_list`):

```yaml
modules:
  dashboards: { module.style: crud, module.cqrs: logical }
```

- `fabric new` adds every module the selected packs bring, in name order
  (`PackModuleTests.Declared_modules_are_recorded_in_name_order`), as
  `fabric add-module <name> --set ...` would: the pins first
  (`PackModuleTests.A_pack_declared_module_is_generated_with_its_pinned_choices`),
  the project's module defaults for the rest
  (`PackModuleTests.A_choice_the_declaration_leaves_open_comes_from_the_module_defaults`).
  They are recorded in `fabric.yaml` like any module, so the module packs
  give each one the normal skeleton. A pack that is not selected brings
  nothing (`PackModuleTests.A_pack_that_is_not_selected_brings_no_module`).
- The declaring pack adds the module's own code as per-module files that
  render for it only: `when: 'module.name == "dashboards"'`
  (`PackModuleTests.The_declaring_pack_contributes_files_for_its_module_only`).
- A pin the catalog does not allow is a generation error naming the pack: an
  unknown option
  (`PackModuleTests.A_pinned_option_the_catalog_does_not_have_is_an_error_naming_the_pack`),
  a project-scoped dimension
  (`PackModuleTests.A_pinned_project_scoped_choice_is_an_error_naming_the_pack`),
  a broken `requires` or `conflicts`
  (`PackModuleTests.A_pinned_option_a_catalog_rule_forbids_is_an_error_naming_the_pack`).
  A project option's condition on a module dimension is still checked
  against the module defaults, as `fabric validate` and the catalog lint
  check it (they see no packs), so a brought module cannot satisfy it
  (`PackModuleTests.A_project_rule_on_modules_is_checked_against_the_module_defaults_as_validate_checks_it`).
- Two packs may bring the same module
  (`PackModuleTests.Agreeing_declarations_bring_one_module`); a dimension
  they pin differently (as a set, for a multi-select dimension) is an error
  naming both (`PackModuleTests.Conflicting_declarations_fail_naming_both_packs`,
  `PackModuleTests.Multi_select_pins_that_list_the_same_options_in_another_order_agree`).
- `fabric add-module` refuses a name a selected pack brings
  (`PackModuleTests.Add_module_refuses_a_name_a_pack_brings`).
- A module-scoped pack may not bring a module: the modules decide which
  module-scoped packs are selected, so such a pack would decide its own
  selection. The loader rejects it
  (`PackModuleTests.A_module_scoped_pack_cannot_bring_a_module`).

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
| `module` | `{ name, namespace, values }`, per-module files only |
| `slots["<slot>"]` | The slot's contributions in order, joined by its separator; files only, a slot contribution does not see it |

(`RendererTests.Renders_project_values`,
`RendererTests.The_project_name_and_its_namespace_are_available`,
`RendererTests.A_multi_valued_dimension_renders_as_a_list`,
`RendererTests.Module_defaults_and_the_module_list_are_available`,
`RendererTests.Templates_see_the_projects_settings_by_key`,
`RendererTests.A_per_module_file_sees_its_module`,
`RendererTests.A_slot_contribution_does_not_see_the_slots`.)

Unknown variables are errors naming the pack and the template
(`RendererTests.An_undefined_variable_is_an_error_naming_the_pack_and_template`).
`date`, `timespan` and `math` are not available: the same choices must
always render the same bytes
(`RendererTests.Clock_functions_are_not_available`,
`RendererTests.The_same_input_renders_byte_identical_output`). Output line
endings are LF (`RendererTests.Line_endings_are_normalised_to_lf`);
whitespace-only lines become empty and runs of blank lines collapse to one,
so an empty slot leaves no gap
(`RendererTests.Runs_of_blank_lines_left_by_empty_slots_collapse_to_one`).

## Slots

Contributions are ordered by `order`, then by option id
(`ComposerTests.Slot_contributions_are_ordered_by_order_then_option`), and
joined by the slot's `separator`, a newline unless declared; JSON lists
declare `",\n"` (`RendererTests.Slot_contributions_are_joined_with_the_declared_separator`).
A contribution that renders empty or whitespace-only is dropped before the
join, so it adds no separator
(`RendererTests.An_empty_or_whitespace_only_contribution_adds_no_separator`).
A slot declared twice (`ComposerTests.Declaring_a_slot_twice_is_an_error`), a
contribution to a slot no selected pack declares
(`ComposerTests.Contributing_to_an_undeclared_slot_is_an_error`), and a
cardinality breach
(`ComposerTests.An_exactly_one_slot_without_a_contribution_is_an_error`,
`ComposerTests.A_single_contribution_slot_with_two_contributions_is_an_error`)
are errors.

A contribution may carry `optional: true` when the slot belongs to a
choice the pack may be combined with but does not require, such as a
converter for an event store that only some projects have:

```yaml
slots:
  - { slot: event-store.json-converters, template: templates/converter.sbn, optional: true }
```

If no selected pack declares the slot, the contribution is dropped and
generation goes on
(`OptionalSlotContributionTests.An_optional_contribution_to_a_slot_no_selected_pack_declares_is_dropped`);
if one does, it counts towards the slot's cardinality and renders like any
other (`OptionalSlotContributionTests.An_optional_contribution_to_a_declared_slot_is_rendered_like_any_other`).
Without `optional` (or with `false`) an undeclared slot stays an error
naming the pack and the slot
(`OptionalSlotContributionTests.A_contribution_that_is_not_optional_to_a_slot_no_selected_pack_declares_is_an_error_naming_the_pack_and_the_slot`),
so a misspelled slot name is still caught everywhere it is not marked
optional. Mark a contribution optional only when some valid combination of
choices has no pack declaring its slot.

A generated web app must pass its own `pnpm format:check` (Prettier), so a
contribution to a file under `web/` is written as Prettier prints it at
the depth where it lands; the template adds the trailing comma after the
last entry of a list
(`FrontendTests.The_generated_web_app_passes_its_format_check_and_a_formatting_violation_fails_it`).

Every slot of a generated project, with its file, separator and the packs
that fill it, is in `docs/slots.md`.

### Role slots

A pack shared across the options of another dimension (zod, an auth client)
contributes to slots that dimension's chosen option declares. So that it
finds them whichever option is chosen, the catalog names them as **role
slots** of that dimension (ADR-0008):

```yaml
frontend.framework:
  role-slots:
    web.dependencies: { description: "Entries of the web app's dependencies.", separator: ",\n" }
```

Every pack of an option of that dimension must declare each role slot,
joined by the catalog's separator; the pack loader rejects one that does
not, naming the slot
(`PackLoaderTests.An_option_pack_that_declares_every_role_slot_of_its_dimension_loads`,
`PackLoaderTests.An_option_pack_missing_a_role_slot_of_its_dimension_is_rejected`,
`PackLoaderTests.A_role_slot_declared_with_another_separator_is_rejected`).
A pack may describe the slot more concretely in its own declaration.

Four dimensions have role slots today: `frontend.framework` (16),
`frontend.api-client` (4), `frontend.workspace` (`web.workspace-packages`)
and `module.event-store` (`event-store.json-converters`); the catalog,
`catalog/catalog.yaml`, is the list.

## Template functions

A pattern that runs through every pack is a function, not a branch. Packs
contribute Scriban code (`func ... end`, no `{{ }}`) to the reserved slot
`templates.functions`; the generator evaluates it in front of every
template and slot contribution instead of rendering it
(`RendererTests.Functions_contributed_to_templates_functions_are_available_in_every_template`,
`RendererTests.A_slot_contribution_can_use_the_contributed_functions_too`).
An error in a template still names the template's own line and column
(`RendererTests.An_error_in_a_template_rendered_with_functions_reports_the_template_s_own_line_and_column`).

Write `{{ not_null "services" }}`, never a guard of your own: the chosen
guards pack decides whether that renders `Ensure.That(services).IsNotNull();`
or `ArgumentNullException.ThrowIfNull(services);`.

### Vocabularies

Where a pack's output depends on a choice it does not own, the packs of that
dimension define the same set of functions, a **vocabulary**, and every
other pack calls them instead of naming the library. Another option then
plugs in by defining the same functions, not by editing every pack. A
function a template calls and the chosen pack does not define is an unknown
variable, so generation fails naming it
(`IdentityVocabularyTests.An_identity_provider_without_the_vocabulary_fails_generation_naming_what_it_lacks`).

| Vocabulary | Defined by every pack of | For | Enforced by |
|---|---|---|---|
| guards: `not_null`, `not_blank` | `backend.guards` | argument guards | the functions test above |
| test | `backend.test.framework`, `backend.test.assertions` | test attributes, lifetimes, the run fixture, assertions | `TestVocabularyTests.Every_pack_of_the_dimension_defines_the_whole_vocabulary_and_nothing_else`, `TestVocabularyTests.A_project_generated_with_other_vocabularies_names_neither_xunit_nor_shouldly_in_its_tests` |
| integration-fixture | `backend.test.integration` | reaching the running application, the host as an anonymous or signed-in caller, the database alone | `IntegrationVocabularyTests.Every_pack_of_the_integration_dimension_defines_the_whole_vocabulary_and_nothing_else`, `IntegrationVocabularyTests.A_project_generated_with_other_vocabularies_names_the_aspire_fixture_only_in_its_own_files` |
| identity-provider | `backend.auth` (not `none`), `frontend.auth-client` | where roles and the tenant sit in the token, a test user's sign-in, the browser's access token | `IdentityVocabularyTests.Every_identity_pack_of_the_dimension_defines_the_whole_vocabulary_and_nothing_else`, `IdentityVocabularyTests.A_project_generated_with_other_vocabularies_names_keycloak_and_oidc_client_ts_only_in_their_own_files` |

Each vocabulary test fails when a pack of its dimension defines more or
fewer functions than the vocabulary, and when a template names the library
outside the defining pack's own files. The functions, their arguments and
what each option returns are in `docs/slots.md` (the test,
integration-fixture and identity-provider vocabularies).

## What the generator writes from every pack

- **ADR:** each selected pack's `adr` is rendered to
  `docs/adr/NNNN-<dimension>-<option>.md`, numbered in selection order, base
  first (`GeneratorTests.Every_selected_pack_gets_its_adr_numbered_in_selection_order`).
- **AGENTS.md:** each selected pack's `agents-md` is a contribution to
  `agents.sections`, in selection order
  (`GeneratorTests.Every_selected_pack_contributes_its_agents_md_to_agents_md_in_selection_order`).
- **Briefs:** each brief a pack gives for a role is a contribution to
  `agents.brief.<role>`
  (`GeneratorTests.A_role_brief_collects_the_briefs_packs_give_for_that_role_rendered_as_templates`).

Those slots are reserved: the base declares them, the generator fills
them, and a pack contributing to them directly is an error
(`ComposerTests.A_pack_may_not_contribute_to_a_slot_the_generator_fills`).
All three are templates and see the same model as any other.

## Adding a module later

`fabric add-module` only creates files. If a module chooses an option
whose pack the project does not use yet, and that pack has project files
or slot contributions, add-module refuses: that content would have to be
merged into files that may have changed
(`AddModuleTests.Rejects_an_option_whose_pack_would_change_existing_files`).
Keep project-wide content of module options small, or expect it to be
chosen at generation time.

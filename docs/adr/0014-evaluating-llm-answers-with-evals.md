# ADR-0014: Evaluating LLM answers with evals, and option settings

**Status:** Accepted
**Date:** 2026-11-05

## Context

ADR-0010 gives a project one `IChatClient` and makes every test use a stand-in
for it (rule 4). That keeps tests deterministic and free, but nothing checks
whether the project's real model still answers its use cases well: a prompt
change, a model upgrade or a provider's own change can make answers worse while
every test stays green. ADR-0010 and ADR-0012 left evaluations for later, as
their own decision.

The repository owner planned it on 2026-11-01 (issue #85) and decided on
2026-11-05:

- `evals` is an option of the multi-select `backend.llm.extras` (ADR-0012).
- The eval job uses the project's own real provider and its API-key secret. It
  is off by default and runs on a schedule or by hand. Ollama stays an option
  for running evals locally.
- Regular tests keep using the fake client.
- A score below a threshold set in `fabric.yaml` fails the eval job.

The same day, the owner accepted as written a batch of eight recommendations
covering the parts this left open (comment on #85).

Until now, `fabric.yaml` records only choices: one option, or a set of options,
per dimension. A threshold is a number. A dimension with one option per value
cannot hold it, because every option needs a pack (ADR-0002) and a threshold of
3.5 would need an option of its own.

## Decision

**A project that calls a model may evaluate its answers, against a threshold it
sets in `fabric.yaml`.**

- **`backend.llm.extras` gains `evals`.** It is `experimental` with no pack, so
  `fabric new` fails with `no pack for backend.llm.extras=evals.` until the pack
  lands (#192). It requires nothing beyond a model: `rag` and `evals` combine.

- **An option may declare settings.** This is a new catalog field
  (docs/catalog-rules.md): numbers the option needs besides being chosen, each
  with a `default`, a `minimum` and a `maximum`:

  ```yaml
  evals:
    tier: experimental
    settings:
      threshold: { default: 4, minimum: 1, maximum: 5 }
  ```

  The linter refuses:
  - a default outside the range;
  - a minimum above the maximum;
  - an id that is not lower-case kebab-case (the id becomes a key segment, so
    it holds no dot);
  - a setting on a module-scoped dimension, because the map is project-wide.

  The field is general. The generator has no rule about `evals` in particular,
  just as `default-from` has none about embeddings (ADR-0012).

- **`fabric.yaml` records settings in a top-level `settings:` map**, after
  `project`, keyed `<dimension>.<option>.<setting>`
  (`backend.llm.extras.evals.threshold: 4`).
  - It is written only when a chosen option declares a setting. No profile and
    no existing project gains a line.
  - `fabric new --set backend.llm.extras.evals.threshold=3.5` sets a value; a
    setting not set takes its default.
  - Each of these is an error naming the key:
    - a value out of range;
    - a value that is not a number;
    - a setting the option does not declare;
    - a value for an option that is not chosen.
  - `fabric add-module` keeps the map and checks it against the catalog again,
    so a value edited by hand out of range is reported, prefixed `fabric.yaml:`.
  - `--set` of a setting during `add-module` is refused: it belongs to the
    project, and is changed in `fabric.yaml`.
  - Templates see `settings["<key>"]`.

- **The eval project reads the threshold from `fabric.yaml` when it runs**, so
  `fabric.yaml` stays the only place it lives and changing it needs no
  regeneration. This does not conflict with ADR-0001: a setting is a number the
  project uses, not a switch between options. The code does not branch on it.

- **What the threshold applies to.** The quality evaluators score each response
  from 1 to 5. For every evaluator metric, the **mean** over a use case's
  examples must reach the threshold, default 4. Every score is still reported.
  A per-example minimum would fail the job on one noisy judgement from a judge
  that is not deterministic.

**Settled for the pack (#192)**, accepted by the owner on 2026-11-05. Facts
were read that day from the NuGet registration API and the packages' nuspecs
and XML documentation.

- **Packages:** `Microsoft.Extensions.AI.Evaluation`, `.Quality` and
  `.Reporting` 10.11.0, published 2026-10-14. Each depends on exactly the
  `Microsoft.Extensions.AI(.Abstractions)` 10.11.0 the base already pins.
  `Microsoft.Extensions.AI.Evaluation.Console` 10.11.0 is a local dotnet tool,
  for the report (`dotnet aieval report`). `.Safety` and `.NLP` are not used:
  both are preview only.
- **Evaluators:**
  - `RelevanceEvaluator` on every example.
  - `GroundednessEvaluator` on examples that supply grounding context
    (`GroundednessEvaluatorContext`), as a `rag` use case does.
  - A project-defined check is an `IEvaluator` in the eval project.
  - The judge is the project's own `IChatClient`. The evaluators' documentation
    says they are tuned on GPT-4o and that their performance "can be especially
    poor when a smaller / local model is used", which matters for small
    defaults such as `llama3.2:1b`.
- **The CI job:** the pack owns `.github/workflows/evals.yml`.
  - It runs by hand (`workflow_dispatch`) or weekly on a `schedule`. The
    scheduled run is skipped unless the repository variable `EVALS_SCHEDULED`
    is `true`, so it is off by default.
  - The secret is named after the provider: `ANTHROPIC_API_KEY`,
    `OPENAI_API_KEY`, or `AZURE_OPENAI_API_KEY` with the variable
    `AZURE_OPENAI_ENDPOINT`. It is passed to the provider's Aspire parameter.
  - With `ollama`, the job runs the `ollama/ollama` image the provider pack
    pins (0.34.4) as a service, pulls the model, and needs no secret.
  - The report is uploaded as an artifact. The eval tests stay out of the
    regular CI test run.
- **Proving it without a model:** a fake judge `IChatClient` in `tests/Fakes`
  answers in the evaluators' response format. The pack's check runs the
  generated eval project twice:
  - with high scores, which passes and writes a report;
  - with a score below the threshold, which fails, naming the metric, the mean
    and the threshold.

  A recorded `DiskBasedResponseCacheProvider` cache is not used, because its
  keys hash the evaluators' prompts and would break on every package update.

**Delivered in two parts:**

1. The catalog option, option settings, the `fabric.yaml` map and this ADR
   (#85).
2. The `evals` pack (#192).

## Consequences

- No profile changes. Every profile takes `backend.llm: none`, so
  `backend.llm.extras` is inactive, no option with settings is chosen, and
  every profile generates byte for byte what it did before.
- `fabric.yaml` is no longer only choices. Its header still says the choices
  are fixed. Settings are the part of it a project may edit, and `add-module`
  checks them.
- A tool that reads a generated project's `fabric.yaml` strictly meets a new
  top-level key only in a project that chose an option with settings. None
  does until the `evals` pack lands.
- What the threshold cannot catch: an evaluator that scores generously, a
  judge model too small to judge, and examples that do not cover the use case.
  An eval run costs money and sends its examples and answers to the provider.

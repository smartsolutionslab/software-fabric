{{~ provider = project["backend.llm"] ~}}
# LLM evals: Microsoft.Extensions.AI.Evaluation

**Status:** Accepted (generated)

## Decision

The project evaluates the answers of its LLM use cases with `Microsoft.Extensions.AI.Evaluation` (ADR-0014 of the generator): a set of examples per use case, answered by the project's own model and scored against a threshold in `fabric.yaml`.

- **`tests/Evals` is the eval project**, with `Microsoft.Extensions.AI.Evaluation`, `.Quality` and `.Reporting` 11.0.0. `UseCases.All` lists the use cases, each with its examples; the sample stands in for a module's.
- **Every example is scored for relevance** (`RelevanceEvaluator`), **for groundedness where it supplies context** (`GroundednessEvaluator` with `GroundednessEvaluatorContext`), and by the project's own checks, each an `IEvaluator` scoring from 1 to 5 (`AnsweredEvaluator` is the sample).
- **The judge is the project's own `IChatClient`**, registered as the host registers it. The quality evaluators are tuned on GPT-4o, and their documentation warns that their judgement "can be especially poor when a smaller / local model is used"{{ if provider == "ollama" }}, which matters for this project's `llama3.2:1b`{{ end }}.
- **The judge is asked without `Temperature` or `TopP`**: the evaluators would send 0 and 1, which some models refuse, so every provider's defaults apply. A score that is not a finite number from 1 to 5, or missing, fails the run, naming the use case, the metric and the example, before any mean is taken.
- **The threshold is `settings.backend.llm.extras.evals.threshold` in `fabric.yaml`**, read when the evals run, so it lives in one place and changing it needs no regeneration. For every metric, the mean over a use case's examples must reach it; every score is still reported. A per-example minimum would fail on one noisy judgement.
- **`.github/workflows/evals.yml` runs the evals**, by hand (`workflow_dispatch`) or weekly when the repository variable `EVALS_SCHEDULED` is `true`; the scheduled run is skipped otherwise, so the evals are off by default.
{{~ if provider == "anthropic" ~}}
  It passes the repository secret `ANTHROPIC_API_KEY` to the AppHost's parameter `anthropic-api-key`.
{{~ else if provider == "openai" ~}}
  It passes the repository secret `OPENAI_API_KEY` to the AppHost's parameter `openai-api-key`.
{{~ else if provider == "azure-openai" ~}}
  It passes the repository secret `AZURE_OPENAI_API_KEY` and the variable `AZURE_OPENAI_ENDPOINT` to the AppHost's parameters `azure-openai-api-key` and `azure-openai-endpoint`.
{{~ else ~}}
  It runs `ollama/ollama:0.34.4`, the image the AppHost runs, as a service, pulls the model and needs no secret.
{{~ end ~}}
  The results are uploaded as an artifact with the HTML report of `dotnet aieval report` (`Microsoft.Extensions.AI.Evaluation.Console` 11.0.0, a local tool).
- **The regular tests never call a model**: they do not run `tests/Evals`, whose name does not end in `.Tests`. They run `tests/Evals.Tests`, which runs the same eval code with a stand-in model and the fake judge `tests/Fakes/FakeJudge.cs`.

## Consequences

- `tests/Evals.Tests` fails the build when a run with high scores does not pass or writes no results, when a mean below the threshold does not fail naming the use case, the metric, the mean and the threshold, when groundedness is scored for an example without context, or when the threshold is not read from `fabric.yaml`. It cannot tell whether the examples cover a use case.
- What the threshold cannot catch: a judge that scores generously, a judge too small to judge, and examples that do not cover what a use case is for.
- Every run costs money and sends the examples and the answers to the provider. Examples hold no personal or secret data.
- The eval project's default model is the AppHost's; a model changed in `src/AppHost/AppHost.cs` is changed in `tests/Evals/ProjectModel.cs` too.

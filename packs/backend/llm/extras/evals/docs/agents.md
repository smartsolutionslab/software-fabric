{{~ provider = project["backend.llm"] ~}}
## LLM evals: Microsoft.Extensions.AI.Evaluation

- A use case that asks the model something gets an entry in `tests/Evals/UseCases.cs`: a name, how it answers an example (through the module's handler; reference its Application project), and examples that cover what it is for. Give an example its grounding context (`new EvalExample(input, context)`) when the answer must stay true to records, as a `rag` use case's must; it is then judged for groundedness too. The answer must be given those records, as the sample passes them to the model; judging an answer against records the model never saw scores a guess.
- A check of the project's own is an `IEvaluator` in `tests/Evals` that scores every answer from 1 to 5 as a `NumericMetric`; add it to the evaluators in `EvalRun`. The threshold applies to its mean as to the others'.
- The threshold is `settings.backend.llm.extras.evals.threshold` in `fabric.yaml`, from 1 to 5: the mean of every metric over a use case's examples must reach it. Change it there, never in code; lowering it to make a failing run pass is a decision for a developer, not a fix.
- `tests/Evals` calls the real model and costs money. Never add it to the regular tests or `ci.yml`, never rename it to `*.Tests`, and never run it in a pull request's checks; `.github/workflows/evals.yml` runs it, by hand or weekly when the repository variable `EVALS_SCHEDULED` is `true`.
{{~ if provider == "ollama" ~}}
- Locally, run it against an Ollama server on port 11434 (`Ollama__Endpoint` and `Ollama__Model` override the server and the model): `dotnet test --project tests/Evals/Evals.csproj`, then `dotnet tool restore && dotnet aieval report --path TestResults/evals --output TestResults/evals/report.html`.
{{~ else ~}}
- Locally, it reads the key from the AppHost's user secrets: `dotnet test --project tests/Evals/Evals.csproj`, then `dotnet tool restore && dotnet aieval report --path TestResults/evals --output TestResults/evals/report.html`.
{{~ end ~}}
- Test the eval code itself in `tests/Evals.Tests` with `FakeJudge` (`tests/Fakes`), which answers in the evaluators' format with a fixed score. No test calls a real model, the judge included.
- The eval project's default model is the AppHost's: change both together.

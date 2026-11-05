using System.Text.RegularExpressions;
using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm.extras: evals</c> (ADR-0014): an eval project that scores the project's LLM use cases with
/// <c>Microsoft.Extensions.AI.Evaluation</c> against its own model, judged by that model, with the threshold read from
/// <c>fabric.yaml</c> when it runs; a workflow that runs it by hand or weekly when opted in; and a gate, in the regular
/// tests, that runs the eval code with a fake judge. No test here calls a model.
/// </summary>
public sealed partial class EvalsTests
{
    private const string Profile = "lean-crud";
    private const string Evals = "backend.llm.extras=evals";
    private const string EvalProject = "tests/Evals/Evals.csproj";
    private const string GateProject = "tests/Evals.Tests/Evals.Tests.csproj";
    private const string Workflow = ".github/workflows/evals.yml";
    private const string ScheduledOnlyWhenOptedIn = "github.event_name != 'schedule' || vars.EVALS_SCHEDULED == 'true'";

    private const string BillingUseCases = """
        using Microsoft.Extensions.AI;
        using Shop.Modules.Billing.Application.Queries.AskTheModel;

        namespace Shop.Evals;

        public static class BillingUseCases
        {
            public static EvalUseCase Ask { get; } = new(
                "billing-ask-the-model",
                async (model, example, cancellationToken) =>
                {
                    var answer = await new AskTheModelHandler(model).HandleAsync(new AskTheModel(example.Input), cancellationToken);
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer.Value));
                },
                [new EvalExample("What does an invoice record?")]);
        }
        """;

    private const string OnlyTheBillingUseCase = """
        namespace Shop.Evals;

        public static class UseCases
        {
            public static IReadOnlyList<EvalUseCase> All { get; } = [BillingUseCases.Ask];
        }
        """;

    public static TheoryData<string> Providers { get; } = ["anthropic", "openai", "azure-openai", "ollama"];

    [Theory]
    [MemberData(nameof(Providers))]
    public void A_project_with_evals_generates_from_the_real_packs_alone(string provider)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [$"backend.llm={provider}", Evals]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_eval_project_references_the_evaluation_packages_and_the_report_tool_at_one_version()
    {
        var files = Files("backend.llm=anthropic", Evals);

        foreach (var package in new[] { "Microsoft.Extensions.AI.Evaluation", "Microsoft.Extensions.AI.Evaluation.Quality", "Microsoft.Extensions.AI.Evaluation.Reporting" })
        {
            Count(files["Directory.Packages.props"], $"<PackageVersion Include=\"{package}\" Version=\"10.11.0\" />").ShouldBe(1, package);
            files[EvalProject].ShouldContain($"<PackageReference Include=\"{package}\" />");
        }

        var tools = files[".config/dotnet-tools.json"];
        tools.ShouldContain("\"microsoft.extensions.ai.evaluation.console\": {");
        tools.ShouldContain("\"version\": \"10.11.0\"");
        tools.ShouldContain("\"commands\": [\"aieval\"]");
        files.Keys.ShouldContain("tests/Fakes/FakeJudge.cs");
        files["Solution.slnx"].ShouldContain(EvalProject);
        files["Solution.slnx"].ShouldContain(GateProject);
    }

    [Fact]
    public void The_eval_project_judges_every_example_for_relevance_and_examples_with_context_for_groundedness()
    {
        var files = Files("backend.llm=anthropic", Evals);
        var sources = string.Join('\n', files.Where(file => file.Key.StartsWith("tests/Evals/", StringComparison.Ordinal)).Select(file => file.Value));

        sources.ShouldContain("new RelevanceEvaluator()");
        sources.ShouldContain("new GroundednessEvaluator()");
        sources.ShouldContain("new GroundednessEvaluatorContext(");
        sources.ShouldContain(": IEvaluator");
        sources.ShouldContain("backend.llm.extras.evals.threshold");
        sources.ShouldNotContain("FakeJudge");
    }

    [Fact]
    public void The_regular_tests_never_run_the_eval_project()
    {
        var files = Files("backend.llm=anthropic", Evals);

        // test.sh, which ci.yml runs, takes every *.Tests.csproj under tests/: the eval project is not one, its gate is.
        files[".github/scripts/test.sh"].ShouldContain("-name '*.Tests.csproj'");
        EvalProject.ShouldNotEndWith(".Tests.csproj");
        GateProject.ShouldEndWith(".Tests.csproj");
        files[".github/workflows/ci.yml"].ShouldNotContain("Evals");
        files[".github/workflows/ci.yml"].ShouldNotContain("aieval");
    }

    [Fact]
    public void The_evals_workflow_runs_by_hand_or_weekly_only_when_the_repository_opts_in_and_uploads_the_report()
    {
        var workflow = Yaml(Files("backend.llm=anthropic", Evals)[Workflow]);
        var on = Map(workflow, "on");
        var job = Map(Map(workflow, "jobs"), "evals");

        on.Children.ShouldContainKey(new YamlScalarNode("workflow_dispatch"));
        var schedule = (YamlMappingNode)((YamlSequenceNode)on[new YamlScalarNode("schedule")]).Children.ShouldHaveSingleItem();
        var cron = Scalar(schedule, "cron");
        CronIsWeekly().IsMatch(cron).ShouldBeTrue(cron);
        Scalar(job, "if").ShouldBe(ScheduledOnlyWhenOptedIn);

        var steps = Steps(job);
        steps.ShouldContain(step => step.Contains("dotnet test --project tests/Evals/Evals.csproj", StringComparison.Ordinal));
        steps.ShouldContain(step => step.Contains("dotnet aieval report", StringComparison.Ordinal));
        steps.ShouldContain(step => step.Contains("actions/upload-artifact@", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("anthropic", "Parameters__anthropic-api-key", "${{ secrets.ANTHROPIC_API_KEY }}")]
    [InlineData("openai", "Parameters__openai-api-key", "${{ secrets.OPENAI_API_KEY }}")]
    [InlineData("azure-openai", "Parameters__azure-openai-api-key", "${{ secrets.AZURE_OPENAI_API_KEY }}")]
    [InlineData("azure-openai", "Parameters__azure-openai-endpoint", "${{ vars.AZURE_OPENAI_ENDPOINT }}")]
    public void The_workflow_passes_the_secret_named_after_the_provider_to_its_aspire_parameter(string provider, string variable, string value)
    {
        var job = Map(Map(Yaml(Files($"backend.llm={provider}", Evals)[Workflow]), "jobs"), "evals");

        Scalar(Map(job, "env"), variable).ShouldBe(value);
        job.Children.ShouldNotContainKey(new YamlScalarNode("services"));
    }

    [Fact]
    public void With_ollama_the_workflow_runs_the_pinned_server_as_a_service_pulls_the_model_and_needs_no_secret()
    {
        var text = Files("backend.llm=ollama", Evals)[Workflow];
        var job = Map(Map(Yaml(text), "jobs"), "evals");

        Scalar(Map(Map(job, "services"), "ollama"), "image").ShouldBe("ollama/ollama:0.34.4");
        Scalar(Map(job, "env"), "Ollama__Model").ShouldBe("llama3.2:1b");
        Steps(job).ShouldContain(step => step.Contains("/api/pull", StringComparison.Ordinal));
        text.ShouldNotContain("secrets.");
    }

    [Theory]
    [InlineData("anthropic", "\"anthropic-model\", \"claude-haiku-5-5\"", "\"anthropic-model\", \"claude-haiku-5-5\"")]
    [InlineData("openai", "\"openai-model\", \"gpt-5-mini\"", "\"openai-model\", \"gpt-5-mini\"")]
    [InlineData("azure-openai", "\"azure-openai-deployment\", \"gpt-5-mini\"", "\"azure-openai-deployment\", \"gpt-5-mini\"")]
    [InlineData("ollama", "\"ollama-model\", \"llama3.2:1b\"", "\"Ollama:Model\", \"llama3.2:1b\"")]
    public void The_eval_project_defaults_to_the_model_the_apphost_defaults_to(string provider, string appHostDefault, string evalDefault)
    {
        var files = Files($"backend.llm={provider}", Evals);

        files["src/AppHost/AppHost.cs"].ShouldContain(appHostDefault);
        files["tests/Evals/ProjectModel.cs"].ShouldContain(evalDefault);
    }

    [Fact]
    public void Fabric_yaml_records_the_threshold_the_eval_project_reads()
    {
        Files("backend.llm=openai", Evals)["fabric.yaml"].ShouldContain("settings:\n  backend.llm.extras.evals.threshold: 4\n");
        Files("backend.llm=openai", Evals, "backend.llm.extras.evals.threshold=3.5")["fabric.yaml"].ShouldContain("  backend.llm.extras.evals.threshold: 3.5\n");
    }

    [Fact]
    public void No_profile_changes_because_none_calls_a_model()
    {
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var files = ProfileFiles(profile);
            files.Keys.ShouldNotContain(path => path.Contains("Evals", StringComparison.Ordinal) || path.Contains("FakeJudge", StringComparison.Ordinal), profile);
            files.Keys.ShouldNotContain(Workflow, profile);
            files["fabric.yaml"].ShouldNotContain("settings:", customMessage: profile);
        }
    }

    [Fact]
    public void The_generated_gate_passes_above_the_threshold_writing_a_report_and_fails_below_it_naming_metric_mean_and_threshold()
    {
        using var project = NewProject("backend.llm=anthropic", Evals);
        project.Build();

        var (exitCode, output) = project.Test(GateProject);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 17");
    }

    [Fact]
    public void A_module_use_case_referenced_from_the_eval_project_alone_builds_and_passes_the_gate_without_grounded_examples()
    {
        using var project = NewProject("backend.llm=anthropic", Evals);
        project.AddModule("billing");
        var evalProject = Path.Combine(project.ProjectDirectory, EvalProject);
        File.WriteAllText(evalProject, File.ReadAllText(evalProject).Replace(
            "<ProjectReference Include=\"../../src/BuildingBlocks/BuildingBlocks.csproj\" />",
            "<ProjectReference Include=\"../../src/BuildingBlocks/BuildingBlocks.csproj\" />\n    <ProjectReference Include=\"../../src/Modules/Billing/Billing.Application/Billing.Application.csproj\" />",
            StringComparison.Ordinal));
        project.WriteTypes("tests/Evals", BillingUseCases);
        project.Write("tests/Evals/UseCases.cs", OnlyTheBillingUseCase);
        project.Build();

        var (exitCode, output) = project.Test(GateProject);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_eval_project_reads_the_threshold_from_fabric_yaml_when_it_runs_not_when_it_was_built()
    {
        using var project = NewProject("backend.llm=anthropic", Evals, "backend.llm.extras.evals.threshold=3.5");
        project.WriteTypes("tests/Evals.Tests", """
            namespace Shop.Evals.Tests;

            public sealed class ThresholdProbe
            {
                [Fact]
                public void The_threshold_is_the_one_fabric_yaml_holds_now() => Shop.Evals.EvalThreshold.Read().ShouldBe(2.5);
            }
            """);
        project.Build();
        var fabricYaml = Path.Combine(project.ProjectDirectory, "fabric.yaml");
        File.WriteAllText(fabricYaml, File.ReadAllText(fabricYaml).Replace("threshold: 3.5", "threshold: 2.5", StringComparison.Ordinal));

        var (exitCode, output) = project.Test(GateProject);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_eval_project_calls_the_projects_own_model_and_without_its_key_fails_naming_the_parameter()
    {
        using var project = NewProject("backend.llm=anthropic", Evals);
        project.Build();

        var (exitCode, output) = project.Test(EvalProject, new Dictionary<string, string> { ["Parameters__anthropic-api-key"] = string.Empty });

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("anthropic-api-key");
    }

    private static Dictionary<string, string> Files(params string[] overrides) => ProfileFiles(Profile, overrides);

    private static Dictionary<string, string> ProfileFiles(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static int Count(string text, string line) =>
        text.Split('\n').Count(candidate => candidate.Trim() == line);

    private static YamlMappingNode Yaml(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlMappingNode Map(YamlMappingNode node, string key) => (YamlMappingNode)node[new YamlScalarNode(key)];

    private static string Scalar(YamlMappingNode node, string key) => ((YamlScalarNode)node[new YamlScalarNode(key)]).Value!;

    /// <summary>Each step of a job as text: its <c>uses</c> and its <c>run</c>.</summary>
    private static List<string> Steps(YamlMappingNode job) =>
        [.. ((YamlSequenceNode)job[new YamlScalarNode("steps")]).Children.Cast<YamlMappingNode>()
            .Select(step => string.Join('\n', step.Children.Where(entry => entry.Key.ToString() is "uses" or "run").Select(entry => entry.Value.ToString())))];

    /// <summary>A lean-crud project with the overrides; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(params string[] overrides)
    {
        var project = new GeneratedProject(Profile, overrides);
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count > 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty();
        return project;
    }

    /// <summary>Minute, hour, any day of the month, any month, one day of the week.</summary>
    [GeneratedRegex(@"^\d{1,2} \d{1,2} \* \* [0-6]$")]
    private static partial Regex CronIsWeekly();
}

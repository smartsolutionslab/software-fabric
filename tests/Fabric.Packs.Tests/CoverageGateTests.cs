using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The coverage gate the xUnit pack ships: the backend CI runs the tests with code coverage, and each module's Domain
/// and Application, and the Kernel, keep the line coverage tests/coverage.props sets, whatever the test discipline.
/// </summary>
public sealed class CoverageGateTests
{
    private const string Profile = "lean-crud";
    private const string TestStep = "run: bash .github/scripts/test.sh";
    private const string Thresholds = "tests/coverage.props";

    public static TheoryData<string, string, int> Layers => new()
    {
        { "Domain", "src/Modules/Billing/Billing.Domain", 90 },
        { "Application", "src/Modules/Billing/Billing.Application", 80 },
    };

    /// <summary>
    /// The Kernel's packs: the defaults, and the other option of each dimension that has one with a pack; and
    /// saas-standard-react, whose Wolverine source generator writes code into each module's Application.
    /// </summary>
    public static TheoryData<string, string> FreshProjects => new()
    {
        { Profile, string.Empty },
        { Profile, "backend.guards=bcl backend.absence=nullable backend.result-style=minimal" },
        { "saas-standard-react", string.Empty },
    };

    [Theory]
    [MemberData(nameof(Layers))]
    public void An_untested_method_fails_the_gate_naming_the_layer_and_the_number_and_its_test_makes_it_pass(string layer, string directory, int threshold)
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{directory}/Discount.cs", Discount(layer));
        project.Build();

        var (failedExitCode, failedOutput) = RunTests(project);

        failedExitCode.ShouldNotBe(0, failedOutput);
        CoverageFailure(layer, threshold).IsMatch(failedOutput).ShouldBeTrue(failedOutput);

        project.Write("tests/Modules/Billing.Tests/DiscountTests.cs", DiscountTests(layer));
        project.Build();

        var (exitCode, output) = RunTests(project);

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void The_thresholds_are_the_ones_tests_coverage_props_sets()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Discount.cs", Discount("Domain"));
        project.Build();

        var thresholds = File.ReadAllText(Path.Combine(project.ProjectDirectory, Thresholds));
        thresholds.ShouldContain("<CoverageThresholdDomain>90</CoverageThresholdDomain>");
        thresholds.ShouldContain("<CoverageThresholdApplication>80</CoverageThresholdApplication>");
        thresholds.ShouldContain("<CoverageThresholdKernel>90</CoverageThresholdKernel>");
        project.Write(Thresholds, thresholds.Replace("<CoverageThresholdDomain>90<", "<CoverageThresholdDomain>0<", StringComparison.Ordinal));

        var (exitCode, output) = RunTests(project);

        exitCode.ShouldBe(0, output);
    }

    [Theory]
    [MemberData(nameof(FreshProjects))]
    public void A_fresh_project_with_a_module_passes_the_gate_its_kernel_included(string profile, string overrides)
    {
        using var project = new GeneratedProject(profile, overrides.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = RunTests(project);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("Shop.Kernel: ");
    }

    [Fact]
    public void Test_after_generates_from_the_real_packs_with_the_coverage_gate_and_without_the_red_first_check()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, ["test.discipline=test-after"]);

        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);
        files.ShouldContainKey(".github/scripts/test.sh");
        files.ShouldContainKey(Thresholds);
        files.ShouldNotContainKey(".github/workflows/red-first.yml");
        files[".github/workflows/ci.yml"].ShouldContain(TestStep);
        files["AGENTS.md"].ShouldContain("## Test discipline: test after");
        files["AGENTS.md"].ShouldContain(Thresholds);
    }

    /// <summary>
    /// Runs the script the backend CI's test step runs, on every test project but the integration tests, which need
    /// Docker and measure the host in its own process, not the layers the gate checks.
    /// </summary>
    private static (int ExitCode, string Output) RunTests(GeneratedProject project)
    {
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));
        ci.ShouldContain(TestStep, Case.Sensitive, "the generated backend CI does not run its tests through .github/scripts/test.sh");

        var tests = Path.Combine(project.ProjectDirectory, "tests");
        string[] projects =
        [
            .. Directory.EnumerateFiles(tests, "*.Tests.csproj", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(project.ProjectDirectory, path).Replace('\\', '/'))
                .Where(path => !path.StartsWith("tests/Integration.Tests/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        return Shell.Run(project.ProjectDirectory, "bash", [".github/scripts/test.sh", .. projects]);
    }

    private static Regex CoverageFailure(string layer, int threshold) =>
        new($@"Shop\.Modules\.Billing\.{layer}: \d+(\.\d)? % line coverage, below the {layer} threshold of {threshold} %");

    private static string Discount(string layer) => $$"""
        namespace Shop.Modules.Billing.{{layer}};

        public static class Discount
        {
            public static decimal Apply(decimal amount, decimal percent)
            {
                if (percent <= 0)
                {
                    return amount;
                }

                var reduction = amount * percent / 100;
                var reduced = amount - reduction;
                return reduced < 0 ? 0 : reduced;
            }
        }
        """;

    private static string DiscountTests(string layer) => $$"""
        using Shop.Modules.Billing.{{layer}};

        namespace Shop.Modules.Billing.Tests;

        public class DiscountTests
        {
            [Fact]
            public void No_percent_leaves_the_amount() => Discount.Apply(100m, 0m).ShouldBe(100m);

            [Fact]
            public void A_percent_reduces_the_amount() => Discount.Apply(100m, 10m).ShouldBe(90m);

            [Fact]
            public void More_than_everything_reduces_it_to_zero() => Discount.Apply(100m, 150m).ShouldBe(0m);
        }
        """;
}

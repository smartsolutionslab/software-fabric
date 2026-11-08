using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// The TUnit test framework (<c>backend.test.framework.tunit</c>, #257): the generated tests, written in the test
/// vocabulary, run on TUnit; the run fixtures are shared by the test session and reach the classes that take them; the
/// architecture rules and the coverage gate fail the build as they do with xUnit.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class TUnitTests
{
    private const string Profile = "saas-standard-react";
    private const string TUnit = "backend.test.framework=tunit";
    private const string StandIn = "This option's pack is not written yet.";

    [Fact]
    public void A_project_with_tunit_generates_from_the_real_packs_and_names_neither_xunit_nor_nunit()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");

        var props = File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Directory.Build.props"));
        props.ShouldContain("<PackageReference Include=\"TUnit.Engine\"");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Integration.Tests/HostTests.cs"))
            .ShouldContain("[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]");
        var named = Directory.EnumerateFiles(project.ProjectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => OtherFramework().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(project.ProjectDirectory, entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    [Fact]
    public void The_architecture_module_and_kernel_tests_run_on_tunit()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        project.Build();

        foreach (var tests in new[] { "tests/Architecture.Tests/Architecture.Tests.csproj", "tests/Modules/Billing.Tests/Billing.Tests.csproj", "tests/Kernel/Paging.Tests/Paging.Tests.csproj" })
        {
            var (exitCode, output) = project.Test(tests);

            exitCode.ShouldBe(0, output);
            output.ShouldContain("failed: 0");
            output.ShouldNotContain("succeeded: 0");
        }
    }

    [Fact]
    public void A_broken_architecture_rule_fails_the_tunit_tests_naming_it()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Billing.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AdditionalFiles Remove="@(AdditionalFiles)" />
              </ItemGroup>
            </Project>
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Rule_holds");
        output.ShouldContain("GuardBanReachesEveryProject");
        output.ShouldContain("src/Modules/Billing/Billing.Application/Billing.Application.csproj does not read build/guards/BannedSymbols.txt");
    }

    [Fact]
    public void The_integration_tests_take_the_application_started_once_for_the_run_on_tunit()
    {
        using var project = NewProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }

    [Fact]
    public void The_rag_tests_share_the_database_for_the_run_and_set_up_each_test_on_tunit()
    {
        using var project = NewProject("lean-crud", "backend.llm=openai", "backend.llm.extras=rag", "backend.llm.embeddings=openai");
        project.Build();

        var (exitCode, output) = project.Test("tests/Rag.Tests/Rag.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 6");
    }

    /// <summary>
    /// TUnit registers the tests of every test assembly a test project loads; the evals' gate references tests/Evals,
    /// whose own test calls the real model, and must never run it. TUnit has no way to leave a loaded assembly's test
    /// out of a run, so it is reported skipped.
    /// </summary>
    [Fact]
    public void A_test_project_referencing_another_test_project_runs_only_its_own_tests()
    {
        using var project = NewProject("lean-crud", "backend.llm=openai", "backend.llm.extras=evals");
        project.Build();

        var (exitCode, output) = project.Test("tests/Evals.Tests/Evals.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
        output.ShouldNotContain("OpenAI:ApiKey");
        output.ShouldContain("skipped: 1");
    }

    /// <summary>
    /// TUnit runs the architecture rules in parallel, and two of them probe the resilience pipeline (#331). The first
    /// pipeline built in a process sets up the options' <c>RangeAttribute</c>s, and that setup is not thread-safe: a
    /// probe running beside it can fail with an InvalidCastException. So probes started together run one at a time.
    /// </summary>
    [Fact]
    public void Retry_probes_started_together_run_one_at_a_time()
    {
        using var project = NewProject("lean-crud");
        project.Write("tests/Architecture.Tests/RetryProbeConcurrency.cs", """
            using System.Collections.Concurrent;

            namespace Shop.Architecture.Tests;

            public class RetryProbeConcurrency
            {
                [Test]
                public void Retry_probes_started_together_run_one_at_a_time()
                {
                    const int Probes = 8;
                    var inside = 0;
                    ConcurrentBag<int> seen = [];
                    using var start = new Barrier(Probes);
                    List<Thread> threads = [.. Enumerable.Range(0, Probes).Select(_ => new Thread(() =>
                    {
                        start.SignalAndWait();
                        Rules.RetryProbe.Attempts(HttpMethod.Get, Rules.RetryProbe.Failure.BadRequest, client =>
                        {
                            seen.Add(Interlocked.Increment(ref inside));
                            Thread.Sleep(TimeSpan.FromMilliseconds(50));
                            Interlocked.Decrement(ref inside);
                            return client;
                        });
                    }))];
                    threads.ForEach(thread => thread.Start());
                    threads.ForEach(thread => thread.Join());

                    if (seen.Max() != 1)
                    {
                        throw new InvalidOperationException($"{seen.Max()} retry probes ran at once.");
                    }
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Architecture.Tests/Architecture.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--treenode-filter", "/*/*/RetryProbeConcurrency/*");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("succeeded: 1");
    }

    /// <summary>The coverage gate is the test framework's, the same with any: TUnit's pack carries xUnit's files unchanged.</summary>
    [Theory]
    [InlineData("coverage.props.sbn")]
    [InlineData("coverage-gate.proj.sbn")]
    [InlineData("test.sh.sbn")]
    [InlineData("ci-step.sbn")]
    public void The_coverage_gate_is_the_xunit_packs_file_for_file(string template)
    {
        var tunit = Path.Combine(Repository.Packs.Packs["backend.test.framework.tunit"].Directory, "templates", template);
        var xunit = Path.Combine(Repository.Packs.Packs["backend.test.framework.xunit"].Directory, "templates", template);

        File.ReadAllText(tunit).ShouldBe(File.ReadAllText(xunit), $"{template} drifted from the xUnit pack's");
    }

    /// <summary>A project with TUnit; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string profile, params string[] more)
    {
        var project = new GeneratedProject(profile, [TUnit, .. more]);
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains(StandIn, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count > 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty();
        return project;
    }

    [GeneratedRegex(@"(?i)\b[xn]unit\b|\[(Fact|Theory|InlineData|TestCase|SetUp)\b|\bIAsyncLifetime\b|\bAssemblyFixture\b|\bRunFixture\b")]
    private static partial Regex OtherFramework();
}

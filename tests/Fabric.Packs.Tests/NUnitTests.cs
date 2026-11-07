using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// The NUnit test framework (<c>backend.test.framework.nunit</c>, #256): the generated tests, written in the test
/// vocabulary, run on NUnit; the run fixtures start once per test run and reach the classes that take them; the
/// architecture rules and the coverage gate fail the build as they do with xUnit.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class NUnitTests
{
    private const string Profile = "saas-standard-react";
    private const string NUnit = "backend.test.framework=nunit";
    private const string StandIn = "This option's pack is not written yet.";

    [Fact]
    public void A_project_with_nunit_generates_from_the_real_packs_and_names_xunit_nowhere()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");

        var props = File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Directory.Build.props"));
        props.ShouldContain("<PackageReference Include=\"NUnit\" />");
        props.ShouldContain("<Using Include=\"NUnit.Framework\"");
        var named = Directory.EnumerateFiles(project.ProjectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => Xunit().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(project.ProjectDirectory, entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    [Fact]
    public void The_architecture_module_and_kernel_tests_run_on_nunit()
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
    public void A_broken_architecture_rule_fails_the_nunit_tests_naming_it()
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
        output.ShouldContain("Rule_holds(\"GuardBanReachesEveryProject\")");
        output.ShouldContain("src/Modules/Billing/Billing.Application/Billing.Application.csproj does not read build/guards/BannedSymbols.txt");
    }

    [Fact]
    public void The_integration_tests_take_the_application_started_once_for_the_run_on_nunit()
    {
        using var project = NewProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }

    [Fact]
    public void The_rag_tests_share_the_database_for_the_run_and_set_up_each_test_on_nunit()
    {
        using var project = NewProject("lean-crud", "backend.llm=openai", "backend.llm.extras=rag", "backend.llm.embeddings=openai");
        project.Build();

        var (exitCode, output) = project.Test("tests/Rag.Tests/Rag.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 6");
    }

    /// <summary>The coverage gate is the test framework's, the same with either: NUnit's pack carries xUnit's files unchanged.</summary>
    [Theory]
    [InlineData("coverage.props.sbn")]
    [InlineData("coverage-gate.proj.sbn")]
    [InlineData("test.sh.sbn")]
    [InlineData("ci-step.sbn")]
    public void The_coverage_gate_is_the_xunit_packs_file_for_file(string template)
    {
        var nunit = Path.Combine(Repository.Packs.Packs["backend.test.framework.nunit"].Directory, "templates", template);
        var xunit = Path.Combine(Repository.Packs.Packs["backend.test.framework.xunit"].Directory, "templates", template);

        File.ReadAllText(nunit).ShouldBe(File.ReadAllText(xunit), $"{template} drifted from the xUnit pack's");
    }

    /// <summary>A project with NUnit; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string profile, params string[] more)
    {
        var project = new GeneratedProject(profile, [NUnit, .. more]);
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

    [GeneratedRegex(@"(?i)\bxunit\b|\[(Fact|Theory|InlineData)\b|\bIAsyncLifetime\b|\bAssemblyFixture\b")]
    private static partial Regex Xunit();
}

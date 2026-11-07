using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// The AwesomeAssertions library (<c>backend.test.assertions.awesome-assertions</c>, #258): the generated tests, written
/// in the test vocabulary, assert with AwesomeAssertions, build and pass; the test framework's own <c>Assert</c> does not
/// compile in tests.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class AwesomeAssertionsTests
{
    private const string Profile = "saas-standard-react";
    private const string AwesomeAssertions = "backend.test.assertions=awesome-assertions";
    private const string StandIn = "This option's pack is not written yet.";

    /// <summary>The test projects that need Docker: the application through Aspire, and the database fixture.</summary>
    private static readonly string[] DockerTests = ["Integration.Tests.csproj", "Rag.Tests.csproj"];

    [Fact]
    public void A_project_with_awesome_assertions_generates_from_the_real_packs_and_names_shouldly_nowhere()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");

        var props = File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Directory.Build.props"));
        props.ShouldContain("<PackageReference Include=\"AwesomeAssertions\"");
        props.ShouldContain("<Using Include=\"AwesomeAssertions\"");
        var named = Directory.EnumerateFiles(project.ProjectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => Shouldly().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(project.ProjectDirectory, entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    [Fact]
    public void The_architecture_module_and_kernel_tests_pass_with_awesome_assertions()
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

    /// <summary>
    /// Every pack's tests that assert, in two projects between them: the AI profile with authorisation, realtime, strict
    /// DDD and both MCP sides; the Vue profile with the minimal result style (orval, the other result style). Each test
    /// project builds, and each one that needs no Docker passes.
    /// </summary>
    [Theory]
    [InlineData("saas-ai-react", new[] { "backend.authorization=roles,claims", "frontend.realtime=signalr", "module.ddd-depth=strict", "backend.mcp=client,server" })]
    [InlineData("saas-standard-vue", new[] { "backend.result-style=minimal" })]
    public void Every_packs_tests_build_and_pass_with_awesome_assertions(string profile, string[] overrides)
    {
        using var project = NewProject(profile, overrides);
        project.AddModule("billing");
        project.Build();

        var testProjects = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "tests"), "*.Tests.csproj", SearchOption.AllDirectories)
            .Where(path => !DockerTests.Contains(Path.GetFileName(path)))
            .Select(path => Path.GetRelativePath(project.ProjectDirectory, path))
            .Order(StringComparer.Ordinal)
            .ToList();
        testProjects.ShouldNotBeEmpty();
        foreach (var tests in testProjects)
        {
            var (exitCode, output) = project.Test(tests);

            exitCode.ShouldBe(0, $"{tests}:{Environment.NewLine}{output}");
            output.ShouldContain("failed: 0", customMessage: tests);
            output.ShouldNotContain("succeeded: 0", customMessage: tests);
        }
    }

    [Fact]
    public void The_integration_tests_pass_with_awesome_assertions()
    {
        using var project = NewProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }

    [Fact]
    public void The_rag_tests_pass_with_awesome_assertions()
    {
        using var project = NewProject("lean-crud", "backend.llm=openai", "backend.llm.extras=rag", "backend.llm.embeddings=openai");
        project.Build();

        var (exitCode, output) = project.Test("tests/Rag.Tests/Rag.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 6");
    }

    [Fact]
    public void A_test_asserting_with_the_frameworks_own_assert_does_not_build()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        project.Write("tests/Modules/Billing.Tests/FrameworkAssertTests.cs", """
            namespace Billing.Tests;

            public sealed class FrameworkAssertTests
            {
                [Fact]
                public void Asserts_with_the_framework() => Xunit.Assert.True(true);
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("RS0030");
        output.ShouldContain("Use AwesomeAssertions (docs/adr)");
    }

    /// <summary>A project with AwesomeAssertions; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string profile, params string[] more)
    {
        var project = new GeneratedProject(profile, [AwesomeAssertions, .. more]);
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

    [GeneratedRegex(@"\bShouldly\b|\.Should(Be|Not|Contain|Start|Have|All)\w*\(|\bShould\.(Throw|NotThrow)")]
    private static partial Regex Shouldly();
}

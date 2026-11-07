using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// The Moq library (<c>backend.test.mocks.moq</c>, #259): a generated project's tests mock with Moq, every pack's tests
/// still build and pass with Moq's namespace imported, and Moq.Analyzers fails the build on a mock that cannot work.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class MoqTests
{
    private const string Profile = "saas-standard-react";
    private const string Moq = "backend.test.mocks=moq";
    private const string StandIn = "This option's pack is not written yet.";

    /// <summary>The test projects that need Docker: the application through Aspire, and the database fixture.</summary>
    private static readonly string[] DockerTests = ["Integration.Tests.csproj", "Rag.Tests.csproj"];

    [Fact]
    public void A_project_with_moq_references_moq_and_its_analyzers_and_names_nsubstitute_nowhere()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");

        var props = File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Directory.Build.props"));
        props.ShouldContain("<PackageReference Include=\"Moq\" />");
        props.ShouldContain("<PackageReference Include=\"Moq.Analyzers\" PrivateAssets=\"all\" />");
        props.ShouldContain("<Using Include=\"Moq\" />");
        var versions = File.ReadAllText(Path.Combine(project.ProjectDirectory, "Directory.Packages.props"));
        versions.ShouldContain("<PackageVersion Include=\"Moq\" Version=\"4.21.0\" />");
        versions.ShouldContain("<PackageVersion Include=\"Moq.Analyzers\" Version=\"0.4.2\" />");
        var named = Directory.EnumerateFiles(project.ProjectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => NSubstitute().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(project.ProjectDirectory, entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
    }

    [Fact]
    public void A_module_test_that_mocks_with_moq_builds_and_passes()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        project.WriteTypes("tests/Modules/Billing.Tests", """
            namespace Billing.Tests;

            /// <summary>A service the test mocks.</summary>
            public interface IExchangeRates
            {
                /// <summary>The rate for a currency.</summary>
                decimal RateFor(string currency);
            }

            public sealed class MockedExchangeRatesTests
            {
                [Fact]
                public void A_mocked_rate_is_returned_and_the_call_verified()
                {
                    var rates = new Mock<IExchangeRates>(MockBehavior.Strict);
                    rates.Setup(r => r.RateFor("EUR")).Returns(1.25m);

                    var rate = rates.Object.RateFor("EUR");

                    rate.ShouldBe(1.25m);
                    rates.Verify(r => r.RateFor("EUR"), Times.Once());
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }

    /// <summary>
    /// Every pack's tests, in two projects between them, build with Moq's namespace imported into every test project
    /// (its <c>Range</c>, <c>Match</c> and <c>Capture</c> sit beside the BCL's), and each one that needs no Docker
    /// passes.
    /// </summary>
    [Theory]
    [InlineData("saas-ai-react", new[] { "backend.authorization=roles,claims", "frontend.realtime=signalr", "module.ddd-depth=strict", "backend.mcp=client,server" })]
    [InlineData("saas-standard-vue", new[] { "backend.result-style=minimal" })]
    public void Every_packs_tests_build_and_pass_with_moq(string profile, string[] overrides)
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
    public void The_integration_tests_pass_with_moq()
    {
        using var project = NewProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }

    [Fact]
    public void A_setup_on_a_member_moq_cannot_intercept_does_not_build()
    {
        using var project = NewProject(Profile);
        project.AddModule("billing");
        project.WriteTypes("tests/Modules/Billing.Tests", """
            namespace Billing.Tests;

            /// <summary>A class whose member cannot be overridden.</summary>
            public class FixedRates
            {
                /// <summary>The rate for a currency.</summary>
                public decimal RateFor(string currency) => 1m;
            }

            public sealed class UninterceptableSetupTests
            {
                [Fact]
                public void Sets_up_a_member_that_is_not_virtual()
                {
                    var rates = new Mock<FixedRates>(MockBehavior.Strict);
                    rates.Setup(r => r.RateFor("EUR")).Returns(2m);

                    rates.Object.RateFor("EUR").ShouldBe(2m);
                }
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Moq1200");
    }

    /// <summary>A project with Moq; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string profile, params string[] more)
    {
        var project = new GeneratedProject(profile, [Moq, .. more]);
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

    [GeneratedRegex(@"\bNSubstitute\b|\bSubstitute\.For\b")]
    private static partial Regex NSubstitute();
}

using System.Text.RegularExpressions;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

/// <summary>
/// The integration-fixture vocabulary (docs/slots.md, M3-12): a pack's generated tests reach the running application,
/// the host as an anonymous or signed-in caller, and the application's database only through
/// <c>templates.functions</c> of the integration pack, and start their fixtures through the test framework's, so
/// another integration option (Testcontainers) or framework plugs in by defining the same functions instead of
/// editing every pack's tests.
/// </summary>
public sealed partial class IntegrationVocabularyTests
{
    private static readonly string[] IntegrationVocabulary =
    [
        "integration_fixture",
        "host_client",
        "signed_in_host_client",
        "running_application",
        "database_fixture",
        "database_connection_string",
        "database_fixture_file",
        "database_fixture_package_references",
        "database_fixture_project_references",
        "integration_fixture_attributes",
        "database_fixture_attributes",
    ];

    private static readonly string[] FrameworkVocabulary =
    [
        "test_attribute",
        "test_cases_attribute",
        "test_case_attribute",
        "test_cancellation_token",
        "write_test_output",
        "test_run_fixture",
        "test_lifetime",
        "test_setup",
        "test_teardown",
        "test_takes_run_fixture",
    ];

    /// <summary>
    /// Every pack with an integration test, and rag's database tests with and without a tenant column. Signalr's and
    /// sse's short-lived test token still reaches the Keycloak resource through the Aspire application (M3-13, the
    /// identity-provider vocabulary), so they come without an identity provider here.
    /// </summary>
    public static TheoryData<string, string[]> Generations => new()
    {
        { "saas-ai-react", ["backend.authorization=roles,claims", "backend.mcp=client,server"] },
        { "saas-ai-react", ["backend.tenancy=none"] },
        { "saas-standard-vue", [] },
        { "lean-crud", ["frontend.realtime=signalr", "backend.auth=none", "backend.tenancy=none"] },
        { "lean-crud", ["frontend.realtime=sse", "backend.auth=none", "backend.tenancy=none"] },
    };

    [Fact]
    public void Every_pack_of_the_integration_dimension_defines_the_whole_vocabulary_and_nothing_else()
    {
        var packs = Repository.Packs.Packs.Values.Where(pack => pack.Dimension == "backend.test.integration").ToList();
        packs.ShouldNotBeEmpty();

        foreach (var pack in packs)
        {
            VocabularyProbe.Functions(pack).ShouldBe(IntegrationVocabulary, ignoreOrder: true, customMessage: $"{pack.Option} must define, in {VocabularyProbe.FunctionsSlot}, exactly the vocabulary of docs/slots.md.");
        }
    }

    [Fact]
    public void No_pack_refuses_its_tests_to_an_integration_option_or_test_framework_other_than_aspire_fixture_and_xunit()
    {
        var refusals = Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*.sbn", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => entry.line.StartsWith("#error", StringComparison.Ordinal) && FixtureOnly().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.path)}:{entry.index + 1}: {entry.line}")
            .ToList();

        refusals.ShouldBeEmpty(string.Join(Environment.NewLine, refusals));
    }

    [Theory]
    [MemberData(nameof(Generations))]
    public void A_project_generated_with_other_vocabularies_names_the_aspire_fixture_only_in_its_own_files(string profile, string[] overrides)
    {
        using var integration = new VocabularyProbe(Repository.Packs.Packs["backend.test.integration.aspire-fixture"], IntegrationVocabulary);
        using var framework = new VocabularyProbe(Repository.Packs.Packs["backend.test.framework.xunit"], FrameworkVocabulary);
        using var project = new GeneratedProject(profile, [integration.Manifest, framework.Manifest], overrides);
        project.AddModule("billing");

        // The integration pack's own files: its fixture, the host's tests and the test project. The MCP client's tests
        // read the AppHost's model and start nothing: Aspire is the composition root whatever the integration option.
        var ownFiles = new[] { "tests/Integration.Tests/AspireFixture.cs", "tests/Integration.Tests/HostTests.cs", "tests/Integration.Tests/Integration.Tests.csproj" };
        var named = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "tests"), "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(path => (path, relative: Path.GetRelativePath(project.ProjectDirectory, path).Replace('\\', '/')))
            .Where(file => !ownFiles.Contains(file.relative, StringComparer.Ordinal) && !file.relative.StartsWith("tests/Mcp.Tests/", StringComparison.Ordinal))
            .Select(file => file.path)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => AspireFixture().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(project.ProjectDirectory, entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Integration.Tests/ListEndpointsArePagedTests.cs"))
            .ShouldContain("using var client = host_client();");
    }

    /// <summary>
    /// A framework that does not inject a run fixture into a test class's constructor by itself (NUnit) needs an
    /// attribute on the class: every class taking a fixture carries the fixture's attributes, which the stand-in
    /// vocabulary renders right before the declaration.
    /// </summary>
    [Theory]
    [MemberData(nameof(Generations))]
    public void Every_test_class_taking_a_fixture_carries_the_fixtures_attributes(string profile, string[] overrides)
    {
        using var integration = new VocabularyProbe(Repository.Packs.Packs["backend.test.integration.aspire-fixture"], IntegrationVocabulary);
        using var project = new GeneratedProject(profile, [integration.Manifest], overrides);
        project.AddModule("billing");

        var declarations = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "tests"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Select(entry => (entry.path, entry.line, entry.index, declaration: FixtureTakingClass().Match(entry.line)))
            .Where(entry => entry.declaration.Success)
            .ToList();

        declarations.ShouldNotBeEmpty();
        var bare = declarations
            .Where(entry => !entry.line.StartsWith($"{entry.declaration.Groups[1].Value}_attributes()public ", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(project.ProjectDirectory, entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        bare.ShouldBeEmpty(string.Join(Environment.NewLine, bare));
    }

    [GeneratedRegex(@"\bclass \w+\((integration_fixture|database_fixture)\(\)\)")]
    private static partial Regex FixtureTakingClass();

    [GeneratedRegex(@"aspire-fixture|Aspire fixture|xUnit only|for xUnit and")]
    private static partial Regex FixtureOnly();

    [GeneratedRegex(@"\bAspireFixture\b|\bAspireDatabase\b|CreateHostClient|\baspire\.|\bdatabase\.ConnectionString\b|Aspire\.Hosting\.Testing|AppHost\.csproj|DistributedApplication")]
    private static partial Regex AspireFixture();
}

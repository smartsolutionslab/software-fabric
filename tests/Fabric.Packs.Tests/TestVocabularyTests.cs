using System.Text.RegularExpressions;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

/// <summary>
/// The test vocabulary (docs/slots.md, M3-11): a pack's generated tests name their test attributes and assertions only
/// through <c>templates.functions</c> of the test-framework and assertion packs, so another framework or assertion
/// library plugs in by defining the same functions instead of editing every pack's tests.
/// </summary>
public sealed partial class TestVocabularyTests
{
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

    private static readonly string[] AssertionVocabulary =
    [
        "should_be",
        "should_be_ignoring_order",
        "should_be_true",
        "should_be_false",
        "should_be_null",
        "should_not_be_null",
        "should_be_empty",
        "should_not_be_empty",
        "should_contain",
        "should_not_contain",
        "should_start_with",
        "should_be_greater_than",
        "should_be_in_range",
        "should_all_be",
        "should_be_in_order",
        "should_be_subset_of",
        "should_be_same_as",
        "should_have_single_item",
        "should_be_of_type",
        "should_be_assignable_to",
        "should_throw",
        "should_throw_async",
        "should_not_throw",
    ];

    public static TheoryData<string, string[]> Generations => new()
    {
        { "saas-ai-react", ["backend.authorization=roles,claims", "frontend.realtime=signalr", "module.ddd-depth=strict", "backend.mcp=client,server"] },
        { "saas-standard-vue", [] },
        { "lean-crud", [] },
    };

    [Theory]
    [InlineData("backend.test.framework")]
    [InlineData("backend.test.assertions")]
    public void Every_pack_of_the_dimension_defines_the_whole_vocabulary_and_nothing_else(string dimension)
    {
        var vocabulary = dimension == "backend.test.framework" ? FrameworkVocabulary : AssertionVocabulary;
        var packs = Repository.Packs.Packs.Values.Where(pack => pack.Dimension == dimension).ToList();
        packs.ShouldNotBeEmpty();

        foreach (var pack in packs)
        {
            VocabularyProbe.Functions(pack).ShouldBe(vocabulary, ignoreOrder: true, customMessage: $"{pack.Option} must define, in {VocabularyProbe.FunctionsSlot}, exactly the vocabulary of docs/slots.md.");
        }
    }

    [Fact]
    public void No_pack_refuses_its_tests_to_an_assertion_library_other_than_shouldly()
    {
        var refusals = Templates()
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => entry.line.StartsWith("#error", StringComparison.Ordinal) && ShouldlyOnly().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.path)}:{entry.index + 1}: {entry.line}")
            .ToList();

        refusals.ShouldBeEmpty(string.Join(Environment.NewLine, refusals));
    }

    [Theory]
    [MemberData(nameof(Generations))]
    public void A_project_generated_with_other_vocabularies_names_neither_xunit_nor_shouldly_in_its_tests(string profile, string[] overrides)
    {
        using var framework = new VocabularyProbe(Pack("backend.test.framework.xunit"), FrameworkVocabulary);
        using var assertions = new VocabularyProbe(Pack("backend.test.assertions.shouldly"), AssertionVocabulary);
        using var project = new GeneratedProject(profile, [framework.Manifest, assertions.Manifest], overrides);
        project.AddModule("billing");

        // The framework pack's own files (its architecture-rule theory, each module's container test) are its to write.
        var frameworkFiles = new[] { "ArchitectureRulesTests.cs", "ModuleTests.cs" };
        var named = Directory.EnumerateFiles(project.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !frameworkFiles.Any(name => path.EndsWith(name, StringComparison.Ordinal)))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => XunitOrShouldly().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(project.ProjectDirectory, entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        named.ShouldBeEmpty(string.Join(Environment.NewLine, named));
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests/Modules/Billing.Tests/BillingModuleTests.cs"))
            .ShouldContain("should_not_throw(buildContainer)");
    }

    private static PackManifest Pack(string option) => Repository.Packs.Packs[option];

    private static IEnumerable<string> Templates() =>
        Directory.EnumerateFiles(Path.Combine(Repository.Root, "packs"), "*.sbn", SearchOption.AllDirectories);

    [GeneratedRegex(@"Shouldly only so far")]
    private static partial Regex ShouldlyOnly();

    [GeneratedRegex(@"\[(Fact|Theory|InlineData)\b|\bAssemblyFixture\b|\bIAsyncLifetime\b|TestContext\.Current|\.Should[A-Z]\w*(<[^()]*>)?\(|\bShould\.[A-Z]")]
    private static partial Regex XunitOrShouldly();

}

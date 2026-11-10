using System.Text.RegularExpressions;

namespace Fabric.Core.Tests.Docs;

/// <summary>
/// docs/packs.md is the contract a pack author reads (M4-32): every section names what enforces its rules, a test as
/// <c>`ComposerTests.Some_rule`</c> or a CI script or workflow as <c>`build/name.sh`</c> or <c>`.github/workflows/name.yml`</c>,
/// and every name it gives exists. A rule whose test is renamed or deleted then fails here instead of pointing nowhere.
/// </summary>
public partial class PackGuideTests
{
    private static readonly string Guide = File.ReadAllText(RepositoryRoot.File("docs", "packs.md")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static readonly string[] TestSources = [.. Directory
        .EnumerateFiles(RepositoryRoot.File("tests"), "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj" or "fixtures"))
        .Select(File.ReadAllText)];

    [Fact]
    public void Every_section_names_what_enforces_its_rules()
    {
        List<string> unenforced = [.. Sections()
            .Where(section => !TestReference().IsMatch(section.Body) && !ScriptReference().IsMatch(section.Body))
            .Select(section => section.Heading)];

        unenforced.ShouldBeEmpty("each section of docs/packs.md names a test (`<Class>Tests.<Method>`) or a CI script (`build/…`, `.github/…`) that enforces it");
    }

    [Fact]
    public void Every_test_the_guide_names_exists()
    {
        List<string> missing = [.. TestReference().Matches(Guide)
            .Select(match => (Name: match.Value.Trim('`'), Class: match.Groups["class"].Value, Method: match.Groups["method"].Value))
            .Where(reference => !TestSources.Any(source => Declares(source, reference.Class, reference.Method)))
            .Select(reference => reference.Name)
            .Distinct(StringComparer.Ordinal)];

        missing.ShouldBeEmpty("docs/packs.md names tests that no test project declares");
    }

    [Fact]
    public void Every_script_the_guide_names_exists()
    {
        List<string> missing = [.. ScriptReference().Matches(Guide)
            .Select(match => match.Groups["path"].Value)
            .Where(path => !File.Exists(RepositoryRoot.File([.. path.Split('/')])))
            .Distinct(StringComparer.Ordinal)];

        missing.ShouldBeEmpty("docs/packs.md names CI scripts that do not exist");
    }

    private static bool Declares(string source, string type, string method) =>
        Regex.IsMatch(source, $@"\bclass\s+{type}\b") &&
        Regex.IsMatch(source, $@"\[(Fact|Theory)[^\]]*\][\s\S]{{0,400}}?\bpublic\s+(async\s+)?(void|Task)\s+{method}\s*\(");

    /// <summary>The level-2 and level-3 sections, each with its text up to the next heading; code blocks are not headings.</summary>
    private static List<(string Heading, string Body)> Sections()
    {
        List<(string Heading, string Body)> sections = [];
        var inCode = false;
        foreach (var line in Guide.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
            }

            if (!inCode && (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal)))
            {
                sections.Add((line, string.Empty));
            }
            else if (sections.Count > 0)
            {
                sections[^1] = (sections[^1].Heading, sections[^1].Body + line + "\n");
            }
        }

        return sections;
    }

    [GeneratedRegex(@"`(?<class>[A-Z][A-Za-z0-9]*Tests)\.(?<method>[A-Za-z0-9_]+)`")]
    private static partial Regex TestReference();

    [GeneratedRegex(@"`(?<path>(?:build|\.github)/[A-Za-z0-9_./-]+\.(?:sh|yml))`")]
    private static partial Regex ScriptReference();
}

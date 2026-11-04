using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The tests run in shards, in parallel (ci.yml): build/test-shards.txt names the classes of each shard, one more
/// shard runs everything the file does not name, and build/tests-all-ran.sh fails the build job unless every test
/// method of every assembly is in some shard's report. A shard can be lost; a test cannot.
/// </summary>
public sealed class TestShardTests : IDisposable
{
    private const string TestType = "Fabric.Packs.Tests.TestShardTests";

    private static readonly string ShardFile = Path.Combine(Repository.Root, "build", "test-shards.txt");

    /// <summary>This assembly's test methods, as the runner lists them: <c>Namespace.Class.Method</c>.</summary>
    private static readonly Lazy<string[]> Methods = new(() =>
    {
        var (exitCode, output) = DotNet.Run(Repository.Root, new Dictionary<string, string>(), typeof(TestShardTests).Assembly.Location, "-list", "methods");
        exitCode.ShouldBe(0, output);
        return [.. output.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("Fabric.", StringComparison.Ordinal))];
    });

    private readonly string reports = Directory.CreateTempSubdirectory("fabric-reports-").FullName;

    public void Dispose() => Directory.Delete(reports, recursive: true);

    [Fact]
    public void An_assembly_whose_every_test_method_is_in_some_report_passes()
    {
        var methods = Methods.Value;
        WriteReport("shard-1", methods[..(methods.Length / 2)]);
        WriteReport("shard-2", methods[(methods.Length / 2)..]);

        var (exitCode, output) = Check();

        exitCode.ShouldBe(0, output);
        output.ShouldContain($"Fabric.Packs.Tests: {methods.Length} test methods, every one ran", Case.Sensitive, output);
    }

    [Fact]
    public void The_check_prints_each_class_s_time_so_the_shards_can_be_rebalanced()
    {
        WriteReport("shard-1", [$"{TestType}.First", $"{TestType}.Second"], time: "1.25");
        WriteReport("shard-2", ["Fabric.Packs.Tests.OtherTests.Only"], time: "40.5");

        var (_, output) = Check();

        output.ShouldContain("seconds  tests  class (from the reports, slowest first)\n     40.5      1  Fabric.Packs.Tests.OtherTests\n      2.5      2  " + TestType + "\n", Case.Sensitive, output);
    }

    [Fact]
    public void A_test_method_in_no_report_fails_the_check_and_is_named()
    {
        var methods = Methods.Value;
        var lost = $"{TestType}.{nameof(A_test_method_in_no_report_fails_the_check_and_is_named)}";
        WriteReport("shard-1", [.. methods.Where(method => method != lost)]);

        var (exitCode, output) = Check();

        exitCode.ShouldBe(1, output);
        output.ShouldContain($"Fabric.Packs.Tests: these test methods ran in no shard:\n  {lost}", Case.Sensitive, output);
    }

    [Fact]
    public void An_assembly_without_any_report_fails_the_check()
    {
        WriteReport("shard-1", ["Fabric.Other.Tests.SomeTests.Something"], "Fabric.Other.Tests");

        var (exitCode, output) = Check();

        exitCode.ShouldBe(1, output);
        output.ShouldContain("Fabric.Packs.Tests: these test methods ran in no shard:\n", Case.Sensitive, output);
        output.ShouldContain($"\n  {TestType}.{nameof(An_assembly_without_any_report_fails_the_check)}\n", Case.Sensitive, output);
    }

    [Fact]
    public void Every_class_a_shard_names_has_tests_and_is_named_once()
    {
        var classes = Methods.Value.Select(method => method[..method.LastIndexOf('.')]).ToHashSet(StringComparer.Ordinal);
        var named = ShardClasses().SelectMany(shard => shard).ToArray();

        named.ShouldNotBeEmpty();
        named.Where(name => !classes.Contains(name)).ShouldBeEmpty("build/test-shards.txt names classes that have no tests");
        named.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ShouldBeEmpty("build/test-shards.txt names a class twice");
    }

    [Fact]
    public void The_workflow_runs_every_shard_and_the_rest_and_the_build_job_checks_them()
    {
        var jobs = (YamlMappingNode)Workflow()["jobs"];
        var tests = (YamlMappingNode)jobs["tests"];
        var matrix = (YamlMappingNode)((YamlMappingNode)tests["strategy"])["matrix"];
        var shards = ((YamlSequenceNode)matrix["shard"]).Children.Select(node => int.Parse(((YamlScalarNode)node).Value!, System.Globalization.CultureInfo.InvariantCulture));

        shards.ShouldBe(Enumerable.Range(1, ShardClasses().Count + 1));
        var build = (YamlMappingNode)jobs["build"];
        ((YamlScalarNode)build["needs"]).Value.ShouldBe("tests");
        File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml")).ShouldContain("build/tests-all-ran.sh");
    }

    [Fact]
    public void A_shard_past_the_rest_is_refused()
    {
        var past = ShardClasses().Count + 2;

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-shard.sh", past.ToString(System.Globalization.CultureInfo.InvariantCulture));

        exitCode.ShouldBe(1, output);
        output.ShouldContain($"There is no shard {past}", Case.Sensitive, output);
    }

    /// <summary>Each shard's classes; comments and blank lines are not shards.</summary>
    private static List<string[]> ShardClasses() =>
    [
        .. File.ReadAllLines(ShardFile)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
    ];

    private static YamlMappingNode Workflow()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml"))));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private (int ExitCode, string Output) Check() =>
        Shell.Run(Repository.Root, "bash", "build/tests-all-ran.sh", reports, typeof(TestShardTests).Assembly.Location);

    /// <summary>A report in the shape <c>--report-xunit-xml</c> writes, in its own directory as each shard's artifact is.</summary>
    private void WriteReport(string shard, IEnumerable<string> methods, string assembly = "Fabric.Packs.Tests", string time = "0.001")
    {
        var tests = methods.Select(method =>
        {
            var split = method.LastIndexOf('.');
            return $"""<test name="{method}" result="Pass" time="{time}" type="{method[..split]}" method="{method[(split + 1)..]}" />""";
        });
        var directory = Directory.CreateDirectory(Path.Combine(reports, shard)).FullName;
        File.WriteAllText(
            Path.Combine(directory, "report.xunit.xml"),
            $"""<assemblies schema-version="3"><assembly name="/runner/tests/{assembly}/bin/Release/net10.0/{assembly}.dll" total="1"><collection name="c">{string.Concat(tests)}</collection></assembly></assemblies>""");
    }
}

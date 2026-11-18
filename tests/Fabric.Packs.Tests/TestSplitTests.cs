using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The test shards are balanced by the times the tests took (#711), not by hand: the build job records each class's
/// time (build/tests-all-ran.sh --times) into a short history of recent runs (build/test-history.sh), cached for the
/// next run, and that run's split job assigns the classes to the shards by the median of their recorded times
/// (build/test-split.sh). build/test-shards.txt is the split while no history is cached.
/// </summary>
public sealed class TestSplitTests : IDisposable
{
    private const string Packs = "Fabric.Packs.Tests";

    private readonly string work = Directory.CreateTempSubdirectory("fabric-split-").FullName;

    public void Dispose() => Directory.Delete(work, recursive: true);

    [Fact]
    public void The_check_writes_each_class_s_time_and_whether_its_collection_runs_it_alone()
    {
        var reports = Directory.CreateDirectory(Path.Combine(work, "reports")).FullName;
        var methods = Methods();
        var listed = methods.Where(method => method.StartsWith($"{Packs}.{nameof(TestSplitTests)}.", StringComparison.Ordinal)).ToArray();
        var others = methods.Except(listed).ToArray();
        var alone = others[0][..others[0].LastIndexOf('.')];
        var aloneMethods = others.Where(method => method.StartsWith($"{alone}.", StringComparison.Ordinal)).ToArray();
        File.WriteAllText(
            Path.Combine(reports, "report.xunit.xml"),
            $"""<assemblies schema-version="3"><assembly name="/runner/tests/{Packs}/bin/Release/net11.0/{Packs}.dll" total="1">"""
            + $"""<collection name="Test collection for {Packs}.{nameof(TestSplitTests)} (id: 1a2b)">{Tests(listed, "1.5")}</collection>"""
            + $"""<collection name="Fixed ports">{Tests(aloneMethods, "0.5")}</collection>"""
            + $"""<collection name="Test collection for others (id: 3c4d)">{Tests(others.Except(aloneMethods), "0.001")}</collection>"""
            + "</assembly></assemblies>");
        var times = Path.Combine(work, "times.tsv");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/tests-all-ran.sh", "--times", times, reports, typeof(TestSplitTests).Assembly.Location);

        exitCode.ShouldBe(0, output);
        output.ShouldContain($"{Packs}: {methods.Length} test methods, every one ran", Case.Sensitive, output);
        var lines = File.ReadAllLines(times);
        lines.ShouldContain($"{Packs}\t{Packs}.{nameof(TestSplitTests)}\tparallel\t{Seconds(listed.Length * 1.5)}");
        lines.ShouldContain($"{Packs}\t{alone}\tserial\t{Seconds(aloneMethods.Length * 0.5)}");
        lines.ShouldBe([.. lines.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void The_history_keeps_each_class_s_last_five_times_newest_first()
    {
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t10.0 20.0 30.0 40.0 50.0",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t60.0",
            $"{Packs}\t{Packs}.RemovedTests\tparallel\t70.0");
        var times = Write(
            "times.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t65.0",
            $"Fabric.Core.Tests\tFabric.Core.Tests.NewTests\tparallel\t7.5");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-history.sh", history, times);

        exitCode.ShouldBe(0, output);
        output.ShouldBe(
            $"Fabric.Core.Tests\tFabric.Core.Tests.NewTests\tparallel\t7.5\n"
            + $"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0 10.0 20.0 30.0 40.0\n"
            + $"{Packs}\t{Packs}.AgUiTests\tserial\t65.0 60.0\n");
    }

    [Fact]
    public void The_first_history_is_this_run_s_times()
    {
        var times = Write("times.tsv", $"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-history.sh", Path.Combine(work, "missing.tsv"), times);

        exitCode.ShouldBe(0, output);
        output.ShouldBe($"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0\n");
    }

    [Fact]
    public void Without_a_history_the_split_is_build_test_shards_txt()
    {
        var shards = Write("shards.txt", "# a comment", $"{Packs}.AgGridTests {Packs}.AgUiTests", $"{Packs}.AnthropicTests");

        var (exitCode, output, estimates) = Split(Path.Combine(work, "missing.tsv"), shards);

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgGridTests {Packs}.AgUiTests", $"{Packs}.AnthropicTests"]);
        estimates.ShouldContain("No test times are cached", Case.Sensitive, estimates);
    }

    [Fact]
    public void Classes_that_run_alone_are_spread_and_the_slowest_shard_is_made_as_light_as_moves_and_swaps_make_it()
    {
        // Greedily, slowest first: A, B, C each take the lightest shard, D joins B (800 s). Swapping B and C then
        // brings the slowest shard to 700 s. The rest shard holds Fabric.Cli.Tests' class from the start.
        var history = Write(
            "history.tsv",
            $"Fabric.Cli.Tests\tFabric.Cli.Tests.CliTests\tparallel\t100.0",
            $"{Packs}\t{Packs}.AgGridTests\tserial\t600.0",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t500.0",
            $"{Packs}\t{Packs}.AgUiVueTests\tserial\t400.0",
            $"{Packs}\t{Packs}.AnthropicTests\tserial\t300.0");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x", "y"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgGridTests", $"{Packs}.AgUiVueTests {Packs}.AnthropicTests"]);
        estimates.ShouldContain("shard  1: est  10.0 min  (parallel   0.0, serial  10.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est  11.7 min  (parallel   0.0, serial  11.7, classes 2)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  3: est  10.0 min  (parallel   1.7, serial   8.3, classes 2, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void Four_parallel_classes_share_a_shard_as_long_as_the_longest_of_them()
    {
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t600.0",
            $"{Packs}\t{Packs}.AgUiTests\tparallel\t600.0",
            $"{Packs}\t{Packs}.AgUiVueTests\tparallel\t600.0",
            $"{Packs}\t{Packs}.AnthropicTests\tparallel\t600.0",
            $"{Packs}\t{Packs}.AzureOpenAITests\tserial\t600.0");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgGridTests {Packs}.AgUiTests {Packs}.AgUiVueTests {Packs}.AnthropicTests"]);
        estimates.ShouldContain("shard  1: est  10.0 min  (parallel  10.0, serial   0.0, classes 4)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est  10.0 min  (parallel   0.0, serial  10.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void A_class_is_balanced_by_the_median_of_its_times_so_one_slow_runner_does_not_move_it()
    {
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tserial\t1800.0 600.0 600.0",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t600.0 600.0 1800.0 1800.0");

        var (exitCode, _, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        estimates.ShouldContain("shard  1: est  20.0 min  (parallel   0.0, serial  20.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est  10.0 min  (parallel   0.0, serial  10.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void Only_classes_of_this_project_whose_source_still_exists_are_named()
    {
        var history = Write(
            "history.tsv",
            $"Fabric.Cli.Tests\tFabric.Cli.Tests.CliTests\tserial\t900.0",
            $"{Packs}\t{Packs}.NoSuchClassTests\tserial\t900.0",
            $"{Packs}\t{Packs}.AgGridTests\tserial\t60.0",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t50.0");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgGridTests {Packs}.AgUiTests"]);
        output.ShouldNotContain("NoSuchClassTests");
        output.ShouldNotContain("Fabric.Cli.Tests");
    }

    [Fact]
    public void A_split_that_would_leave_a_shard_empty_is_build_test_shards_txt()
    {
        var history = Write("history.tsv", $"{Packs}\t{Packs}.AgGridTests\tserial\t60.0");
        var shards = Write("shards.txt", $"{Packs}.AgUiTests", $"{Packs}.AnthropicTests");

        var (exitCode, output, estimates) = Split(history, shards);

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgUiTests", $"{Packs}.AnthropicTests"]);
        estimates.ShouldContain("would leave a shard empty", Case.Sensitive, estimates);
    }

    [Fact]
    public void A_shard_runs_the_classes_of_the_split_it_is_given()
    {
        var shards = Write("shards.txt", $"{Packs}.AgGridTests");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-shard.sh", "99", Path.Combine(work, "results"), shards);

        exitCode.ShouldBe(1, output);
        output.ShouldContain("There is no shard 99: the split names 1, and shard 2 runs the rest.", Case.Sensitive, output);
    }

    [Fact]
    public void The_workflow_splits_once_for_every_shard_and_the_build_job_records_the_times_for_the_next_run()
    {
        var jobs = (YamlMappingNode)Workflow()["jobs"];
        var split = Steps(jobs, "split");
        var tests = (YamlMappingNode)jobs["tests"];
        var build = Steps(jobs, "build");

        split.ShouldContain(step => Scalar(step, "uses") != null && Scalar(step, "uses")!.StartsWith("actions/cache/restore@", StringComparison.Ordinal));
        split.ShouldContain(step => Scalar(step, "run") != null && Scalar(step, "run")!.Contains("build/test-split.sh", StringComparison.Ordinal));
        ((YamlScalarNode)tests["needs"]).Value.ShouldBe("split");
        Steps(jobs, "tests").ShouldContain(step => Scalar(step, "run") != null && Scalar(step, "run")!.Contains("build/test-shard.sh \"${{ matrix.shard }}\" \"$RUNNER_TEMP/test-results\" \"$RUNNER_TEMP/test-shards.txt\"", StringComparison.Ordinal));
        var check = build.FindIndex(step => Scalar(step, "run") is { } run && run.Contains("build/tests-all-ran.sh --times", StringComparison.Ordinal));
        var record = build.FindIndex(step => Scalar(step, "run") is { } run && run.Contains("build/test-history.sh", StringComparison.Ordinal));
        var save = build.FindIndex(step => Scalar(step, "uses") is { } uses && uses.StartsWith("actions/cache/save@", StringComparison.Ordinal));
        check.ShouldBeGreaterThan(-1);
        record.ShouldBeGreaterThan(check);
        save.ShouldBeGreaterThan(record);
    }

    private static string[] Methods()
    {
        var (exitCode, output) = DotNet.Run(Repository.Root, new Dictionary<string, string>(), typeof(TestSplitTests).Assembly.Location, "-list", "methods");
        exitCode.ShouldBe(0, output);
        return [.. output.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("Fabric.", StringComparison.Ordinal))];
    }

    private static string Seconds(double seconds) => seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private static string Tests(IEnumerable<string> methods, string time) => string.Concat(methods.Select(method =>
    {
        var split = method.LastIndexOf('.');
        return $"""<test name="{method}" result="Pass" time="{time}" type="{method[..split]}" method="{method[(split + 1)..]}" />""";
    }));

    /// <summary>The split's shards: its lines that are not comments.</summary>
    private static string[] Shards(string output) =>
        [.. output.Split('\n').Where(line => line.Length > 0 && !line.StartsWith('#'))];

    private static YamlMappingNode Workflow()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml"))));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static List<YamlMappingNode> Steps(YamlMappingNode jobs, string job) =>
        [.. ((YamlSequenceNode)((YamlMappingNode)jobs[job])["steps"]).Children.Cast<YamlMappingNode>()];

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;

    private (int ExitCode, string Output, string Estimates) Split(string history, string shards)
    {
        var estimates = Path.Combine(work, "estimates.txt");
        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "-c", "build/test-split.sh \"$0\" \"$1\" 2> \"$2\"", history, shards, estimates);
        return (exitCode, output, File.Exists(estimates) ? File.ReadAllText(estimates) : string.Empty);
    }

    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(work, name);
        File.WriteAllLines(path, lines);
        return path;
    }
}

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
        lines.ShouldContain($"{Packs}\t{Packs}.{nameof(TestSplitTests)}\tparallel\t{Seconds(listed.Length * 1.5)}\t0.00\t1");
        lines.ShouldContain($"{Packs}\t{alone}\tserial\t{Seconds(aloneMethods.Length * 0.5)}\t0.00\t1");
        lines.ShouldContain($"{Packs}\t{Part(listed[0])}\tparallel\t1.5\t0.00\t1");
        lines.ShouldContain($"{Packs}\t{Part(aloneMethods[0])}\tserial\t0.5\t0.00\t1");
        lines.ShouldBe([.. lines.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void The_check_writes_each_class_s_load_the_tests_beside_it_in_its_shard_and_how_many_shards_ran_it()
    {
        // TestSplitTests' methods run one after another, 10 s each, from a minute before midnight; one method of
        // another class runs beside the first half of them, and that class's other methods run in a second shard.
        var reports = Directory.CreateDirectory(Path.Combine(work, "reports")).FullName;
        var methods = Methods();
        var listed = methods.Where(method => method.StartsWith($"{Packs}.{nameof(TestSplitTests)}.", StringComparison.Ordinal)).ToArray();
        var beside = methods.Except(listed).GroupBy(method => method[..method.LastIndexOf('.')]).First(group => group.Count() > 1).ToArray();
        var besideClass = beside[0][..beside[0].LastIndexOf('.')];
        var others = methods.Except(listed).Except(beside).ToArray();
        var start = new DateTimeOffset(2026, 11, 18, 23, 59, 0, TimeSpan.Zero);
        var half = listed.Length * 5;
        Directory.CreateDirectory(Path.Combine(reports, "a"));
        Directory.CreateDirectory(Path.Combine(reports, "b"));
        File.WriteAllText(
            Path.Combine(reports, "a", "report.xunit.xml"),
            $"""<assemblies schema-version="3"><assembly name="/runner/tests/{Packs}/bin/Release/net11.0/{Packs}.dll" total="1">"""
            + $"""<collection name="Test collection for {Packs}.{nameof(TestSplitTests)} (id: 1a2b)">"""
            + string.Concat(listed.Select((method, index) => Timed(method, start.AddSeconds(index * 10), 10)))
            + "</collection>"
            + $"""<collection name="Test collection for {besideClass} (id: 5e6f)">{Timed(beside[0], start, half)}</collection>"""
            + "</assembly></assemblies>");
        File.WriteAllText(
            Path.Combine(reports, "b", "report.xunit.xml"),
            $"""<assemblies schema-version="3"><assembly name="/runner/tests/{Packs}/bin/Release/net11.0/{Packs}.dll" total="1">"""
            + $"""<collection name="Test collection for {besideClass} (id: 5e6f)">{Tests(beside.Skip(1), "1.0")}</collection>"""
            + $"""<collection name="Test collection for others (id: 3c4d)">{Tests(others, "0.001")}</collection>"""
            + "</assembly></assemblies>");
        var times = Path.Combine(work, "times.tsv");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/tests-all-ran.sh", "--times", times, reports, typeof(TestSplitTests).Assembly.Location);

        exitCode.ShouldBe(0, output);
        var lines = File.ReadAllLines(times);
        var besideSeconds = half + beside.Length - 1;
        lines.ShouldContain($"{Packs}\t{Packs}.{nameof(TestSplitTests)}\tparallel\t{Seconds(listed.Length * 10)}\t0.50\t1");
        lines.ShouldContain($"{Packs}\t{Part(listed[0])}\tparallel\t10.0\t1.00\t1");
        lines.ShouldContain($"{Packs}\t{Part(listed[^1])}\tparallel\t10.0\t0.00\t1");
        lines.ShouldContain($"{Packs}\t{besideClass}\tparallel\t{Seconds(besideSeconds)}\t{(half / (double)besideSeconds).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}\t2");
        lines.ShouldContain($"{Packs}\t{Part(beside[0])}\tparallel\t{Seconds(half)}\t1.00\t1");
    }

    [Fact]
    public void The_history_keeps_each_class_s_last_five_times_newest_first_each_with_its_load_and_parts()
    {
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t10.0 20.0 30.0 40.0 50.0\t0.10 0.20 0.30 0.40 0.50\t1 2 1 3 1",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t60.0\t0.00\t1",
            $"{Packs}\t{Packs}.RemovedTests\tparallel\t70.0\t1.00\t1");
        var times = Write(
            "times.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0\t2.75\t4",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t65.0\t0.25\t1",
            $"Fabric.Core.Tests\tFabric.Core.Tests.NewTests\tparallel\t7.5\t1.50\t1");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-history.sh", history, times);

        exitCode.ShouldBe(0, output);
        output.ShouldBe(
            $"Fabric.Core.Tests\tFabric.Core.Tests.NewTests\tparallel\t7.5\t1.50\t1\n"
            + $"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0 10.0 20.0 30.0 40.0\t2.75 0.10 0.20 0.30 0.40\t4 1 2 1 3\n"
            + $"{Packs}\t{Packs}.AgUiTests\tserial\t65.0 60.0\t0.25 0.00\t1 1\n");
    }

    [Fact]
    public void The_first_history_is_this_run_s_times()
    {
        var times = Write("times.tsv", $"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0\t0.00\t1");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-history.sh", Path.Combine(work, "missing.tsv"), times);

        exitCode.ShouldBe(0, output);
        output.ShouldBe($"{Packs}\t{Packs}.AgGridTests\tparallel\t5.0\t0.00\t1\n");
    }

    [Fact]
    public void Method_times_are_kept_only_for_a_class_whose_time_was_over_the_target_of_15_minutes_in_any_run()
    {
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t950.0 800.0\t0.00 0.00\t1 1",
            $"{Packs}\t{Packs}.AgGridTests#A\tparallel\t500.0 400.0\t0.00 0.00\t1 1",
            $"{Packs}\t{Packs}.AgGridTests#Gone\tparallel\t100.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiTests\tparallel\t100.0 100.0\t0.00 0.00\t1 1",
            $"{Packs}\t{Packs}.AnthropicTests\tparallel\t100.0 100.0\t0.00 0.00\t1 1");
        var times = Write(
            "times.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t100.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgGridTests#A\tparallel\t60.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgGridTests#B\tparallel\t40.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiTests\tparallel\t1000.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiTests#X\tparallel\t1000.0\t0.00\t1",
            $"{Packs}\t{Packs}.AnthropicTests\tparallel\t899.0\t0.00\t1",
            $"{Packs}\t{Packs}.AnthropicTests#Y\tparallel\t899.0\t0.00\t1");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-history.sh", history, times);

        exitCode.ShouldBe(0, output);
        output.ShouldBe(
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t100.0 950.0 800.0\t0.00 0.00 0.00\t1 1 1\n"
            + $"{Packs}\t{Packs}.AgGridTests#A\tparallel\t60.0 500.0 400.0\t0.00 0.00 0.00\t1 1 1\n"
            + $"{Packs}\t{Packs}.AgGridTests#B\tparallel\t40.0\t0.00\t1\n"
            + $"{Packs}\t{Packs}.AgUiTests\tparallel\t1000.0 100.0 100.0\t0.00 0.00 0.00\t1 1 1\n"
            + $"{Packs}\t{Packs}.AgUiTests#X\tparallel\t1000.0\t0.00\t1\n"
            + $"{Packs}\t{Packs}.AnthropicTests\tparallel\t899.0 100.0 100.0\t0.00 0.00 0.00\t1 1 1\n");
    }

    [Fact]
    public void A_class_with_method_times_runs_in_parts_named_by_method_and_balanced_like_classes()
    {
        var methods = ClassMethods("ModuleScreenAccessibilityTests");
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.ModuleScreenAccessibilityTests\tparallel\t1800.0\t0.00\t1",
            $"{Packs}\t{methods[0]}\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{methods[1]}\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{methods[2]}\tparallel\t600.0\t0.00\t1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x", "y"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([methods[0], methods[1]]);
        estimates.ShouldContain("shard  1: est  10.0 min  (parallel  10.0, serial   0.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est  10.0 min  (parallel  10.0, serial   0.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  3: est  10.0 min  (parallel  10.0, serial   0.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void Parts_of_one_class_in_one_shard_run_one_after_another_so_they_are_spread()
    {
        var methods = ClassMethods("ModuleScreenAccessibilityTests");
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.ModuleScreenAccessibilityTests\tparallel\t1600.0\t0.00\t1",
            $"{Packs}\t{methods[0]}\tparallel\t400.0\t0.00\t1",
            $"{Packs}\t{methods[1]}\tparallel\t400.0\t0.00\t1",
            $"{Packs}\t{methods[2]}\tparallel\t400.0\t0.00\t1",
            $"{Packs}\t{methods[3]}\tparallel\t400.0\t0.00\t1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{methods[0]} {methods[2]}"]);
        estimates.ShouldContain("shard  1: est  13.3 min  (parallel  13.3, serial   0.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est  13.3 min  (parallel  13.3, serial   0.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void A_method_whose_source_is_gone_is_not_named()
    {
        var methods = ClassMethods("ModuleScreenAccessibilityTests");
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.ModuleScreenAccessibilityTests\tparallel\t1000.0\t0.00\t1",
            $"{Packs}\t{Packs}.ModuleScreenAccessibilityTests#No_such_method_anywhere\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{methods[0]}\tparallel\t400.0\t0.00\t1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([methods[0]]);
        output.ShouldNotContain("No_such_method_anywhere");
    }

    [Fact]
    public void A_shard_runs_whole_classes_and_single_methods_together()
    {
        var method = $"{Packs}.{nameof(TestSplitTests)}#{nameof(The_first_history_is_this_run_s_times)}";
        var shards = Write("shards.txt", $"{method} {Packs}.KestrelEndpointsTests");
        var results = Path.Combine(work, "results");

        var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/test-shard.sh", "1", results, shards);

        exitCode.ShouldBe(0, output);
        var ran = Directory.GetFiles(results, "*.xml", SearchOption.AllDirectories)
            .SelectMany(report => System.Xml.Linq.XDocument.Load(report).Descendants("test"))
            .Select(test => $"{test.Attribute("type")!.Value}.{test.Attribute("method")!.Value}")
            .Order(StringComparer.Ordinal);
        string[] expected = [.. Methods().Where(name => name.StartsWith($"{Packs}.KestrelEndpointsTests.", StringComparison.Ordinal)).Append(method.Replace('#', '.')).Order(StringComparer.Ordinal)];
        ran.ToArray().ShouldBe(expected, output);
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
            $"Fabric.Cli.Tests\tFabric.Cli.Tests.CliTests\tparallel\t100.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgGridTests\tserial\t600.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t500.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiVueTests\tserial\t400.0\t0.00\t1",
            $"{Packs}\t{Packs}.AnthropicTests\tserial\t300.0\t0.00\t1");

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
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiTests\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiVueTests\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{Packs}.AnthropicTests\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{Packs}.AzureOpenAITests\tserial\t600.0\t0.00\t1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgGridTests {Packs}.AgUiTests {Packs}.AgUiVueTests {Packs}.AnthropicTests"]);
        estimates.ShouldContain("shard  1: est  10.0 min  (parallel  10.0, serial   0.0, classes 4)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est  10.0 min  (parallel   0.0, serial  10.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void Parallel_slowdown_is_learned_from_a_class_s_times_at_different_loads_and_slows_every_class_beside_another()
    {
        // AgGridTests took 400 s alone and 1000 s beside 2.5 other tests on average: each one beside it slowed it by
        // 0.6 of its time alone. Three classes in one shard then take 1200 s, not the 600 s of the longest of them.
        var history = Write(
            "history.tsv",
            $"Fabric.Cli.Tests\tFabric.Cli.Tests.CliTests\tserial\t900.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgGridTests\tparallel\t1000.0 400.0\t2.50 0.00\t1 1",
            $"{Packs}\t{Packs}.AgUiTests\tparallel\t600.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiVueTests\tparallel\t600.0\t0.00\t1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgUiTests {Packs}.AgUiVueTests {Packs}.AgGridTests"]);
        estimates.ShouldContain("each other test running beside a class slows it by 0.60 of its time alone", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  1: est  20.0 min  (parallel  20.0, serial   0.0, classes 3)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est  15.0 min  (parallel   0.0, serial  15.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void Setup_per_part_is_learned_from_a_class_run_whole_and_in_parts_and_paid_by_every_part()
    {
        // The class took 1200 s whole and 1440 s in three parts, so each part after the first cost 120 s more. Its
        // three methods, equal by their times, then take a third of the rest each, plus the 120 s every part pays.
        var methods = ClassMethods("ModuleScreenAccessibilityTests");
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.ModuleScreenAccessibilityTests\tparallel\t1440.0 1200.0\t0.00 0.00\t3 1",
            $"{Packs}\t{methods[0]}\tparallel\t400.0\t0.00\t1",
            $"{Packs}\t{methods[1]}\tparallel\t400.0\t0.00\t1",
            $"{Packs}\t{methods[2]}\tparallel\t400.0\t0.00\t1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x", "y"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([methods[0], methods[1]]);
        estimates.ShouldContain("a class run in parts pays 120 s again for every part", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  1: est   8.0 min  (parallel   8.0, serial   0.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est   8.0 min  (parallel   8.0, serial   0.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  3: est   8.0 min  (parallel   8.0, serial   0.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void A_class_that_fits_the_target_alone_runs_whole_though_its_methods_have_times()
    {
        // 1500 s beside 2.5 other tests, 600 s alone: its work alone is 600 s, under the 15 minutes, so splitting it
        // would only pay its setup again.
        var methods = ClassMethods("ModuleScreenAccessibilityTests");
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.ModuleScreenAccessibilityTests\tparallel\t1500.0 600.0\t2.50 0.00\t1 1",
            $"{Packs}\t{methods[0]}\tparallel\t750.0 300.0\t2.50 0.00\t1 1",
            $"{Packs}\t{methods[1]}\tparallel\t750.0 300.0\t2.50 0.00\t1 1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.ModuleScreenAccessibilityTests"]);
        estimates.ShouldContain("shard  1: est  10.0 min  (parallel  10.0, serial   0.0, classes 1)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void A_class_whose_work_alone_was_over_the_target_in_any_run_runs_in_parts()
    {
        // Its median, 850 s, fits the 15 minutes, but one run took 1000 s with nothing beside it: a runner that slow
        // again would leave its shard over the target, so it runs in parts.
        var methods = ClassMethods("ModuleScreenAccessibilityTests");
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.ModuleScreenAccessibilityTests\tparallel\t1000.0 700.0\t0.00 0.00\t1 1",
            $"{Packs}\t{methods[0]}\tparallel\t500.0 350.0\t0.00 0.00\t1 1",
            $"{Packs}\t{methods[1]}\tparallel\t500.0 350.0\t0.00 0.00\t1 1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([methods[0]]);
        estimates.ShouldContain("shard  1: est   7.1 min  (parallel   7.1, serial   0.0, classes 1)\n", Case.Sensitive, estimates);
        estimates.ShouldContain("shard  2: est   7.1 min  (parallel   7.1, serial   0.0, classes 1, the rest)\n", Case.Sensitive, estimates);
    }

    [Fact]
    public void A_class_is_balanced_by_the_median_of_its_times_so_one_slow_runner_does_not_move_it()
    {
        var history = Write(
            "history.tsv",
            $"{Packs}\t{Packs}.AgGridTests\tserial\t1800.0 600.0 600.0\t0.00 0.00 0.00\t1 1 1",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t600.0 600.0 1800.0 1800.0\t0.00 0.00 0.00 0.00\t1 1 1 1");

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
            $"Fabric.Cli.Tests\tFabric.Cli.Tests.CliTests\tserial\t900.0\t0.00\t1",
            $"{Packs}\t{Packs}.NoSuchClassTests\tserial\t900.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgGridTests\tserial\t60.0\t0.00\t1",
            $"{Packs}\t{Packs}.AgUiTests\tserial\t50.0\t0.00\t1");

        var (exitCode, output, estimates) = Split(history, Write("shards.txt", "x"));

        exitCode.ShouldBe(0, estimates);
        Shards(output).ShouldBe([$"{Packs}.AgGridTests {Packs}.AgUiTests"]);
        output.ShouldNotContain("NoSuchClassTests");
        output.ShouldNotContain("Fabric.Cli.Tests");
    }

    [Fact]
    public void A_split_that_would_leave_a_shard_empty_is_build_test_shards_txt()
    {
        var history = Write("history.tsv", $"{Packs}\t{Packs}.AgGridTests\tserial\t60.0\t0.00\t1");
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
        string[] keys = [.. split.Concat(build).Where(step => Scalar(step, "uses") is { } uses && uses.StartsWith("actions/cache/", StringComparison.Ordinal))
            .SelectMany(step => ((YamlMappingNode)step["with"]).Children.Where(entry => ((YamlScalarNode)entry.Key).Value is "key" or "restore-keys"))
            .Select(entry => ((YamlScalarNode)entry.Value).Value!)];
        keys.Length.ShouldBe(5);
        keys.ShouldAllBe(key => key.StartsWith("test-times-v2-", StringComparison.Ordinal), "a history without loads and parts is not read");
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

    /// <summary>A class's test methods, as the split names a part: <c>Namespace.Class#Method</c>, in order.</summary>
    private static string[] ClassMethods(string type) =>
        [.. Methods().Where(method => method.StartsWith($"{Packs}.{type}.", StringComparison.Ordinal)).Select(Part).Distinct().Order(StringComparer.Ordinal)];

    /// <summary><c>Namespace.Class.Method</c> as the times and the split name it: <c>Namespace.Class#Method</c>.</summary>
    private static string Part(string method) => $"{method[..method.LastIndexOf('.')]}#{method[(method.LastIndexOf('.') + 1)..]}";

    private static string Seconds(double seconds) => seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private static string Tests(IEnumerable<string> methods, string time) => string.Concat(methods.Select(method =>
    {
        var split = method.LastIndexOf('.');
        return $"""<test name="{method}" result="Pass" time="{time}" type="{method[..split]}" method="{method[(split + 1)..]}" />""";
    }));

    private static string Timed(string method, DateTimeOffset start, int seconds)
    {
        var split = method.LastIndexOf('.');
        var finish = start.AddSeconds(seconds);
        return $"""<test name="{method}" result="Pass" time="{seconds}" start-rtf="{start:yyyy-MM-ddTHH:mm:ss.fffffffzzz}" finish-rtf="{finish:yyyy-MM-ddTHH:mm:ss.fffffffzzz}" type="{method[..split]}" method="{method[(split + 1)..]}" />""";
    }

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

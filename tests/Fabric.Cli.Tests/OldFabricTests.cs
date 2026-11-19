namespace Fabric.Cli.Tests;

/// <summary>
/// The stand-in for a released fabric is started by the tests and by <c>fabric upgrade --from</c>. Linux refuses to start
/// a file that any process holds open for writing (ETXTBSY), and a process forked by a test running in parallel inherits
/// every descriptor this process has open at that moment, until it calls exec. So the file that is started must be one
/// this process never opened for writing, however briefly.
/// </summary>
public sealed class OldFabricTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("fabric-old-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void The_old_fabric_is_started_from_a_file_the_build_wrote_not_the_tests()
    {
        var script = OldFabric.Create(root, "0.0.1", Paths.FixtureCatalog, Paths.FixturePacks);

        var started = File.ResolveLinkTarget(script, returnFinalTarget: true);

        started.ShouldNotBeNull($"{script} is a file the tests wrote, so a process forked meanwhile may still hold it open for writing");
        Path.GetDirectoryName(started.FullName).ShouldBe(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
    }

    [Fact]
    public void The_old_fabric_reports_the_version_it_stands_in_for()
    {
        var script = OldFabric.Create(root, "0.0.1", Paths.FixtureCatalog, Paths.FixturePacks);

        var (exitCode, output, error) = Programs.Run(script, root, ["--version"]);

        exitCode.ShouldBe(0, error);
        output.Trim().ShouldBe("0.0.1");
    }
}

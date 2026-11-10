using System.Reflection;

namespace Fabric.Cli.Tests;

public class VersionOptionTests
{
    /// <summary>SemVer 2.0 without build metadata: a git SHA there would make every checkout write different bytes.</summary>
    private const string SemVer = @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$";

    [Fact]
    public void Prints_the_generators_semver_and_nothing_else()
    {
        var version = typeof(FabricCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var run = CliRun.Of("--version");

        run.ExitCode.ShouldBe(0, run.Error);
        run.Output.ShouldBe($"{version}{Environment.NewLine}");
        version.ShouldMatch(SemVer);
    }
}

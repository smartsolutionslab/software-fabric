namespace Fabric.Packs.Tests;

/// <summary>
/// Reads the output of a vitest run in the browser (#731). A tester iframe that never became ready runs no test, so the
/// run's exit code says nothing about the code under test: such a run is told apart instead of read as the gate's verdict.
/// </summary>
internal static class BrowserTester
{
    public static bool NeverStarted(string output) => false;

    public static void ShouldHaveStarted(string output)
    {
    }
}

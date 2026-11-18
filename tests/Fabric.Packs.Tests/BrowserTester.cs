namespace Fabric.Packs.Tests;

/// <summary>
/// Reads the output of a vitest run in the browser (#731). A tester iframe that never became ready runs no test, so the
/// run's exit code says nothing about the code under test: such a run is told apart instead of read as the gate's verdict.
/// </summary>
internal static class BrowserTester
{
    /// <summary>What vitest's orchestrator says when a test file's iframe did not report ready in time.</summary>
    private const string NotReady = "did not become ready within";

    public static bool NeverStarted(string output) => output.Contains(NotReady, StringComparison.Ordinal);

    /// <summary>Fails, with the whole output, when the browser tester never started; a run that did is left to the caller.</summary>
    public static void ShouldHaveStarted(string output)
    {
        if (NeverStarted(output))
        {
            throw new ShouldAssertException($"the browser tester never started (timeout), not the gate's verdict:\n{output}");
        }
    }
}

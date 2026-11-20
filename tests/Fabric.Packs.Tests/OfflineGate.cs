namespace Fabric.Packs.Tests;

/// <summary>
/// Reads the end-to-end run of an app whose service worker caches /api answers (#782). Only the offline test failing on
/// its last check, an /api answer that came back offline, is the gate's verdict; a run that failed anywhere else says
/// nothing about the worker and fails as itself, with the whole output, which a clipped <c>ShouldContain</c> never shows.
/// </summary>
internal static class OfflineGate
{
    /// <summary>Playwright's summary: the offline test failed, and no other.</summary>
    private const string OfflineTestFailedAlone = "1 failed\n    [chromium] › e2e/offline.spec.ts";

    /// <summary>Its last check, the only one that expects /api to be unreachable, which the worker's cache answered.</summary>
    private const string ApiCheck = "Expected: \"unreachable\"";

    private const string CachedAnswer = "Received: 200";

    public static void ShouldBeItsVerdict(int exitCode, string output)
    {
        if (exitCode == 0)
        {
            throw new ShouldAssertException($"the end-to-end tests passed with a worker that caches /api answers:\n{output}");
        }

        string[] verdict = [OfflineTestFailedAlone, ApiCheck, CachedAnswer];
        if (!verdict.All(line => output.Contains(line, StringComparison.Ordinal)))
        {
            throw new ShouldAssertException($"the broken worker's run failed, but not on the offline test's /api check: not the gate's verdict:\n{output}");
        }
    }
}

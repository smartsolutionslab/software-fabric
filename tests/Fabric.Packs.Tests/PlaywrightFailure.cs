namespace Fabric.Packs.Tests;

/// <summary>What a failed Playwright run leaves in test-results, as text for a test's failure message (#791).</summary>
internal static class PlaywrightFailure
{
    public static string Describe(string testResults) => string.Empty;
}

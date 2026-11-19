using System.IO.Compression;

namespace Fabric.Packs.Tests;

/// <summary>
/// A failed Playwright run explains itself in the test's failure message (#791): what the page showed, the pages it
/// went to, the requests that failed or were refused, and the browser's errors, read from what Playwright leaves in
/// test-results. The fixture is shaped like the files Playwright 1.65.1 writes for a sign-in that never reaches the
/// identity provider's form.
/// </summary>
public sealed class PlaywrightFailureTests : IDisposable
{
    private const string TestDirectory = "sign-in-a-realm-user-signs-in-and-sees-the-application-chromium";

    private const string ErrorContext = """
        # Test info

        - Name: sign-in.spec.ts >> a realm user signs in and sees the application
        - Location: e2e/sign-in.spec.ts:3:1

        # Error details

        ```
        Error: locator.fill: Test timeout of 30000ms exceeded.
        Call log:
          - waiting for getByLabel('Username or email')
        ```

        # Page snapshot

        ```yaml
        - paragraph [ref=e2]: "Sign-in failed: Failed to fetch"
        ```
        """;

    /// <summary>The page's own trace: its console, an uncaught error, and lines the summary has no use for.</summary>
    private const string PageTrace = """
        {"version":10,"type":"context-options","origin":"library","browserName":"chromium","playwrightVersion":"1.65.1","options":{}}
        {"type":"before","callId":"call@45","startTime":977.2,"class":"Frame","method":"goto","params":{"url":"/","waitUntil":"load"}}
        {"type":"console","messageType":"log","text":"[vite] connected.","args":[],"location":{"url":"http://localhost:5173/@vite/client","lineNumber":1,"columnNumber":1},"time":1040.1,"pageId":"page@1"}
        {"type":"console","messageType":"error","text":"Failed to load resource: net::ERR_CONNECTION_REFUSED","args":[],"location":{"url":"http://localhost:8080/realms/shop/.well-known/openid-configuration","lineNumber":0,"columnNumber":0},"time":1048.3,"pageId":"page@1"}
        {"type":"log","callId":"call@47","time":1062.4,"message":"waiting for getByLabel('Username or email')"}
        {"type":"event","time":1264.9,"class":"BrowserContext","method":"pageError","params":{"error":{"error":{"message":"Outdated Optimize Dep","stack":"Error: Outdated Optimize Dep","name":"Error"}},"location":{"url":"http://localhost:5173/src/main.ts","line":0,"column":0}},"pageId":"page@1"}
        """;

    private const string PageNetwork = """
        {"type":"resource-snapshot","snapshot":{"startedDateTime":"2026-11-19T05:33:40.100Z","request":{"method":"GET","url":"http://localhost:5173/"},"response":{"status":200,"statusText":"OK"},"_resourceType":"document"}}
        {"type":"resource-snapshot","snapshot":{"startedDateTime":"2026-11-19T05:33:40.300Z","request":{"method":"GET","url":"http://localhost:5173/src/main.ts"},"response":{"status":200,"statusText":"OK"},"_resourceType":"script"}}
        {"type":"resource-snapshot","snapshot":{"startedDateTime":"2026-11-19T05:33:40.500Z","request":{"method":"GET","url":"http://localhost:5173/node_modules/.vite/deps/oidc-client-ts.js?v=0ab1"},"response":{"status":504,"statusText":"Outdated Optimize Dep"},"_resourceType":"script"}}
        {"type":"resource-snapshot","snapshot":{"startedDateTime":"2026-11-19T05:33:40.700Z","request":{"method":"GET","url":"http://localhost:8080/realms/shop/.well-known/openid-configuration"},"response":{"status":-1,"statusText":""},"_resourceType":"fetch"}}
        {"type":"resource-snapshot","snapshot":{"startedDateTime":"2026-11-19T05:33:41.000Z","request":{"method":"GET","url":"http://localhost:8080/realms/shop/protocol/openid-connect/auth?client_id=shop-web"},"response":{"status":302,"statusText":"Found"},"_resourceType":"document"}}
        """;

    private readonly string testResults = Directory.CreateTempSubdirectory("fabric-playwright-").FullName;

    public PlaywrightFailureTests()
    {
        var test = Directory.CreateDirectory(Path.Combine(testResults, TestDirectory)).FullName;
        File.WriteAllText(Path.Combine(test, "error-context.md"), ErrorContext);
        using var trace = ZipFile.Open(Path.Combine(test, "trace.zip"), ZipArchiveMode.Create);
        Add(trace, "test.trace", """{"version":10,"type":"context-options","origin":"testRunner"}""");
        Add(trace, "0-trace.trace", """{"version":10,"type":"context-options","origin":"library"}""");
        Add(trace, "0-trace.network", string.Empty);
        Add(trace, "1-trace.trace", PageTrace);
        Add(trace, "1-trace.network", PageNetwork);
    }

    public void Dispose() => Directory.Delete(testResults, recursive: true);

    [Fact]
    public void A_failed_tests_error_context_is_quoted_whole_under_its_test()
    {
        var description = PlaywrightFailure.Describe(testResults);

        description.ShouldContain($"--- {TestDirectory}");
        description.ShouldContain(ErrorContext);
    }

    [Fact]
    public void The_trace_names_every_page_the_browser_went_to_in_order_with_its_status()
    {
        var description = PlaywrightFailure.Describe(testResults);

        var app = description.IndexOf("200 GET http://localhost:5173/ (document)", StringComparison.Ordinal);
        var identityProvider = description.IndexOf("302 GET http://localhost:8080/realms/shop/protocol/openid-connect/auth?client_id=shop-web (document)", StringComparison.Ordinal);
        app.ShouldBeGreaterThanOrEqualTo(0, description);
        identityProvider.ShouldBeGreaterThan(app, description);
    }

    [Fact]
    public void The_trace_lists_the_requests_that_failed_or_were_refused_and_leaves_out_the_rest()
    {
        var description = PlaywrightFailure.Describe(testResults);

        description.ShouldContain("504 GET http://localhost:5173/node_modules/.vite/deps/oidc-client-ts.js?v=0ab1 (script)");
        description.ShouldContain("failed GET http://localhost:8080/realms/shop/.well-known/openid-configuration (fetch)");
        description.ShouldNotContain("/src/main.ts (script)");
        description.ShouldContain("5 requests recorded");
    }

    [Fact]
    public void The_trace_lists_the_consoles_errors_and_the_pages_uncaught_errors_but_not_its_other_messages()
    {
        var description = PlaywrightFailure.Describe(testResults);

        description.ShouldContain("error: Failed to load resource: net::ERR_CONNECTION_REFUSED (http://localhost:8080/realms/shop/.well-known/openid-configuration)");
        description.ShouldContain("uncaught: Outdated Optimize Dep (http://localhost:5173/src/main.ts)");
        description.ShouldNotContain("[vite] connected.");
    }

    [Fact]
    public void Without_test_results_it_says_there_are_none()
    {
        var missing = Path.Combine(testResults, "missing");

        PlaywrightFailure.Describe(missing).ShouldBe($"No Playwright test results in {missing}.");
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}

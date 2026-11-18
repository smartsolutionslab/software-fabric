namespace Fabric.Packs.Tests;

/// <summary>A browser tester that never started fails as itself, never as the gate's verdict (#731).</summary>
public sealed class BrowserTesterTests
{
    /// <summary>The ui package's test output from main's run on 9d222cd (job 134527205668), as #731 quotes it.</summary>
    private const string TimedOut = """
         RUN  v5.1.0 /tmp/fabric-saas-GWUTXv/shop/web/packages/ui

         Test Files   (1)
              Tests  no tests
             Errors  1 error
           Duration  61.24s

        Error: Failed to run the test /tmp/fabric-saas-GWUTXv/shop/web/packages/ui/src/Badge.test.ts.
        Caused by: Error: The iframe "/tmp/fabric-saas-GWUTXv/shop/web/packages/ui/src/Badge.test.ts" did not become ready within 60000ms. The tester likely failed to initialize, check the browser console for errors.
        """;

    /// <summary>The gate's own verdict on a badge with a colour of its own: the test ran, and failed.</summary>
    private const string GateFailed = """
         RUN  v5.1.0 /tmp/fabric-saas-Glt2I1/shop/web/packages/ui

         ❯  chromium  src/Badge.test.ts (2 tests | 1 failed) 2104ms
             ✓ a badge shows its label in the tone it is given 41ms
             × a badge is styled only through the design tokens 1803ms

         Test Files  1 failed (1)
              Tests  1 failed | 1 passed (2)
        """;

    private const string Passed = """
         RUN  v5.1.0 /tmp/fabric-saas-GWUTXv/shop/web/packages/ui

         ✓  chromium  src/Badge.test.ts (2 tests) 1987ms

         Test Files  1 passed (1)
              Tests  2 passed (2)
        """;

    [Fact]
    public void A_tester_iframe_that_did_not_become_ready_never_started()
    {
        BrowserTester.NeverStarted(TimedOut).ShouldBeTrue();
    }

    [Theory]
    [InlineData(GateFailed)]
    [InlineData(Passed)]
    public void A_run_whose_tests_ran_started_whether_they_passed_or_failed(string output)
    {
        BrowserTester.NeverStarted(output).ShouldBeFalse();
        Should.NotThrow(() => BrowserTester.ShouldHaveStarted(output));
    }

    [Fact]
    public void A_tester_that_never_started_fails_as_a_timeout_with_the_whole_output_not_as_the_gates_verdict()
    {
        var failure = Should.Throw<ShouldAssertException>(() => BrowserTester.ShouldHaveStarted(TimedOut));

        failure.Message.ShouldStartWith("the browser tester never started (timeout), not the gate's verdict");
        failure.Message.ShouldContain(TimedOut);
    }
}

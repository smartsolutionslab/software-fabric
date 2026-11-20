namespace Fabric.Packs.Tests;

/// <summary>
/// The broken worker's end-to-end run is the gate's verdict only when the offline test fails on its /api check (#782);
/// a run that fails anywhere else fails as itself, with the whole output.
/// </summary>
public sealed class OfflineGateTests
{
    private const string Start = """

        > app@ e2e /tmp/fabric-saas-vXvLt4/shop/web/app
        > playwright test


        Running 2 tests using 2 workers
        """;

    /// <summary>The verdict: offline, the worker answered /api/me from its cache.</summary>
    private const string CachedApiAnswer = Start + """
        ·F

          1) [chromium] › e2e/offline.spec.ts:3:1 › the app shell loads offline, and /api is never answered from the service worker

            Error: expect(received).toBe(expected) // Object.is equality

            Expected: "unreachable"
            Received: 200

              35 |
              36 |   await expect(page.getByRole('heading', { name: 'shop' })).toBeVisible();
            > 37 |   expect(await page.evaluate(status, request)).toBe('unreachable');
                 |                                                ^
              38 | });

          1 failed
            [chromium] › e2e/offline.spec.ts:3:1 › the app shell loads offline, and /api is never answered from the service worker
          1 passed (24.1s)
        The end-to-end tests failed.
        --- AppHost output
        """;

    /// <summary>The offline test gave up before it reached /api: the page never showed the app after the reload.</summary>
    private const string HeadingNeverShown = Start + """
        ·F

          1) [chromium] › e2e/offline.spec.ts:3:1 › the app shell loads offline, and /api is never answered from the service worker

            Error: expect(locator).toBeVisible() failed

            Locator: getByRole('heading', { name: 'shop' })
            Expected: visible
            Timeout: 5000ms
            Error: element(s) not found

              14 |     .toBe(true);
              15 |   await page.reload();
            > 16 |   await expect(page.getByRole('heading', { name: 'shop' })).toBeVisible();
                 |                                                             ^

          1 failed
            [chromium] › e2e/offline.spec.ts:3:1 › the app shell loads offline, and /api is never answered from the service worker
          1 passed (31.7s)
        The end-to-end tests failed.
        --- AppHost output
        """;

    /// <summary>The online read of /api/me failed, so the offline check never ran.</summary>
    private const string OnlineReadFailed = Start + """
        ·F

          1) [chromium] › e2e/offline.spec.ts:3:1 › the app shell loads offline, and /api is never answered from the service worker

            Error: expect(received).toBe(expected) // Object.is equality

            Expected: 200
            Received: "unreachable"

            > 30 |   expect(await page.evaluate(status, request)).toBe(200);
                 |                                                ^

          1 failed
            [chromium] › e2e/offline.spec.ts:3:1 › the app shell loads offline, and /api is never answered from the service worker
          1 passed (19.0s)
        The end-to-end tests failed.
        --- AppHost output
        """;

    /// <summary>Another spec failed and the offline test passed.</summary>
    private const string AnotherSpecFailed = Start + """
        F·

          1) [chromium] › e2e/billing-samples.spec.ts:5:1 › a created sample is listed

            Error: expect(locator).toBeVisible() failed

            Locator: getByRole('cell', { name: 'first' })
            Expected: visible
            Timeout: 5000ms
            Error: element(s) not found

          1 failed
            [chromium] › e2e/billing-samples.spec.ts:5:1 › a created sample is listed
          1 passed (22.4s)
        The end-to-end tests failed.
        --- AppHost output
        """;

    private const string Passed = Start + """
        ··

          2 passed (20.8s)
        """;

    [Fact]
    public void A_run_whose_offline_test_got_an_api_answer_offline_is_the_gates_verdict()
    {
        Should.NotThrow(() => OfflineGate.ShouldBeItsVerdict(1, CachedApiAnswer));
    }

    [Theory]
    [InlineData(HeadingNeverShown)]
    [InlineData(OnlineReadFailed)]
    [InlineData(AnotherSpecFailed)]
    public void A_run_that_failed_anywhere_but_the_offline_api_check_fails_as_itself_with_the_whole_output(string output)
    {
        var failure = Should.Throw<ShouldAssertException>(() => OfflineGate.ShouldBeItsVerdict(1, output));

        failure.Message.ShouldStartWith("the broken worker's run failed, but not on the offline test's /api check: not the gate's verdict");
        failure.Message.ShouldContain(output);
    }

    [Fact]
    public void A_run_that_passed_with_the_broken_worker_fails_as_a_worker_the_gate_did_not_catch()
    {
        var failure = Should.Throw<ShouldAssertException>(() => OfflineGate.ShouldBeItsVerdict(0, Passed));

        failure.Message.ShouldStartWith("the end-to-end tests passed with a worker that caches /api answers");
        failure.Message.ShouldContain(Passed);
    }

    [Fact]
    public void An_exit_code_of_zero_is_not_the_verdict_whatever_the_output_says()
    {
        Should.Throw<ShouldAssertException>(() => OfflineGate.ShouldBeItsVerdict(0, CachedApiAnswer));
    }
}

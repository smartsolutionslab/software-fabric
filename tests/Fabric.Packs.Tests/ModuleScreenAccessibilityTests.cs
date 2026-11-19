using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.test.extras.accessibility (#604): every module screen of a single-page app, <c>web/app/src/modules/</c>,
/// gets an axe check beside it, from <c>fabric new</c> and <c>fabric add-module</c> alike, and the generated
/// <c>src/screens.test.ts</c> fails while a module screen has none. The check is <c>&lt;Name&gt;Page.axe.test.*</c>, so the
/// screen's own component test, <c>&lt;Name&gt;Page.test.*</c>, stays the project's (#627). It always runs, whatever the
/// project's own test says, and stands in for the generated API client itself, so it checks a real screen.
/// </summary>
public sealed class ModuleScreenAccessibilityTests
{
    private static readonly string[] AgentsAndDashboards =
    [
        "backend.llm=openai",
        "backend.llm.agents=agent-framework",
        "frontend.agent-ui=ag-ui",
        "frontend.dashboards=user-editable,generative",
    ];

    /// <summary>A real screen: it asks the generated client who is signed in, and its field has no label.</summary>
    private const string ReactScreen = """
        import { useGetMeQuery } from '@/api/generated';

        export function BillingPage() {
          const { data } = useGetMeQuery();
          return (
            <main>
              <h1>Billing</h1>
              <p>{data?.name ?? 'Who is signed in is still loading.'}</p>
              <input type="text" />
            </main>
          );
        }

        """;

    private const string VueScreen = """
        <script setup lang="ts">
        import { useGetMe } from '@/api/generated';

        const { data } = useGetMe();
        </script>

        <template>
          <main>
            <h1>Billing</h1>
            <p>{{ data?.name ?? 'Who is signed in is still loading.' }}</p>
            <input type="text" />
          </main>
        </template>

        """;

    private const string UnlabelledField = """<input type="text" />""";

    /// <summary>The screen's own test, its axe check skipped.</summary>
    private const string ReactSkippedOwnTest = """
        import { render } from '@testing-library/react';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import { BillingPage } from './BillingPage';

        test.skip('our billing page passes axe', async () => {
          const { container } = render(<BillingPage />);

          expect(await axe(container)).toHaveNoViolations();
        });

        """;

    private const string VueSkippedOwnTest = """
        import { render } from '@testing-library/vue';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import BillingPage from './BillingPage.vue';

        test.skip('our billing page passes axe', async () => {
          const { container } = render(BillingPage);

          expect(await axe(container)).toHaveNoViolations();
        });

        """;

    /// <summary>The screen's own test, its axe check commented out.</summary>
    private const string CommentedOutOwnTest = """
        import { expect, test } from 'vitest';

        test('our billing page is still to be tested', () => {
          // expect(await axe(container)).toHaveNoViolations();
          expect('Billing').toBe('Billing');
        });

        """;

    public static TheoryData<string, string> Routers => new()
    {
        { "saas-standard-react", "frontend.routing=react-router" },
        { "saas-standard-react", "frontend.routing=tanstack-router" },
        { "saas-standard-vue", "frontend.routing=vue-router" },
    };

    [Theory]
    [MemberData(nameof(Routers))]
    public void The_screen_add_module_writes_has_an_axe_check_beside_it(string profile, string routing)
    {
        using var project = new GeneratedProject(profile, routing);
        project.AddModule("billing");

        Screens(project).ShouldNotBeEmpty();
        UncheckedScreens(project).ShouldBeEmpty();
        Screens(project).ShouldAllBe(screen => !File.Exists(Path.Combine(project.ProjectDirectory, OwnTestOf(screen))));
        Screens(project).ShouldAllBe(screen => File.ReadAllText(Path.Combine(project.ProjectDirectory, DataOf(screen))).Contains("export const examples"));
    }

    [Fact]
    public void The_assistant_and_dashboards_screens_have_an_axe_check_beside_them_with_react()
    {
        using var project = new GeneratedProject("saas-ai-react");

        Screens(project).Count.ShouldBe(2);
        UncheckedScreens(project).ShouldBeEmpty();
    }

    [Fact]
    public void The_assistant_and_dashboards_screens_have_an_axe_check_beside_them_with_vue()
    {
        using var project = new GeneratedProject("saas-standard-vue", AgentsAndDashboards);

        Screens(project).Count.ShouldBe(2);
        UncheckedScreens(project).ShouldBeEmpty();
    }

    /// <summary>A screen that shows an unlabelled field only once its data has come.</summary>
    private const string ReactScreenBehindLoading = """
        import { HOOK } from '@/api/generated';

        export function BillingPage() {
          const { isLoading } = HOOK();
          return (
            <main>
              <h1>Billing</h1>
              {isLoading ? <p>Loading</p> : <input type="text" />}
            </main>
          );
        }

        """;

    private const string VueScreenBehindLoading = """
        <script setup lang="ts">
        import { useGetMe } from '@/api/generated';

        const { isLoading } = useGetMe();
        </script>

        <template>
          <main>
            <h1>Billing</h1>
            <p v-if="isLoading">Loading</p>
            <input v-else type="text" />
          </main>
        </template>

        """;

    /// <summary>Each client the axe check stands in for, by the hook of the generated client that asks who is signed in.</summary>
    public static TheoryData<string, string[], string> Clients => new()
    {
        { "saas-standard-react", ["frontend.routing=react-router"], "useGetMeQuery" },
        { "saas-standard-react", ["frontend.routing=tanstack-router"], "useGetMeQuery" },
        { "saas-standard-react", ["frontend.state=tanstack-query", "frontend.api-client=orval"], "useGetMe" },
        { "saas-standard-vue", ["frontend.routing=vue-router"], "useGetMe" },
    };

    [Theory]
    [MemberData(nameof(Clients))]
    public void Once_its_data_file_has_an_example_the_axe_check_also_checks_the_screen_as_it_shows_that_data(
        string profile,
        string[] overrides,
        string hook)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");
        // The screen is the test's, so the module page's sample goes, its tests and data with it (#734).
        project.DeleteSample("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var vue = screen.EndsWith(".vue", StringComparison.Ordinal);
        var generated = TestOf(screen);
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");
        var behindLoading = vue ? VueScreenBehindLoading : ReactScreenBehindLoading.Replace("HOOK", hook, StringComparison.Ordinal);
        var example = $$"""
            export const examples: Record<string, unknown> = { {{hook}}: { name: 'Ada' } };

            """;

        project.Write(screen, behindLoading);
        project.Write(DataOf(screen), example);
        var (populatedExitCode, populatedOutput, populated) = Statuses(project);
        populatedExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label behind the loading branch:\n{populatedOutput}");
        populated.GetValueOrDefault(generated).ShouldBe(AxeChecks(vue, "passed", "passed", "failed", "failed"), populatedOutput);

        project.Write(screen, behindLoading.Replace(UnlabelledField, """<input type="text" aria-label="Invoice number" />""", StringComparison.Ordinal)
            .Replace("<input v-else type=\"text\" />", "<input v-else type=\"text\" aria-label=\"Invoice number\" />", StringComparison.Ordinal));
        var (labelledExitCode, labelledOutput, labelled) = Statuses(project);
        labelledExitCode.ShouldBe(0, $"pnpm test:\n{labelledOutput}");
        labelled.GetValueOrDefault(generated).ShouldBe(AxeChecks(vue, "passed", "passed", "passed", "passed"));

        project.Write(screen, behindLoading);
        project.Write(DataOf(screen), "export const examples: Record<string, unknown> = {};\n");
        var (loadingExitCode, loadingOutput, loading) = Statuses(project);
        loadingExitCode.ShouldBe(0, $"pnpm test:\n{loadingOutput}");
        loading.GetValueOrDefault(generated).ShouldBe(AxeChecks(vue, "passed", "passed", "skipped", "skipped"));
    }

    /// <summary>A screen that shows an unlabelled field only once RTK Query's <c>currentData</c> has come.</summary>
    private const string ReactScreenBehindCurrentData = """
        import { useGetMeQuery } from '@/api/generated';

        export function BillingPage() {
          const { currentData } = useGetMeQuery();
          return (
            <main>
              <h1>Billing</h1>
              {currentData ? <input type="text" /> : <p>Loading</p>}
            </main>
          );
        }

        """;

    /// <summary>A screen that shows an unlabelled field only once its lazy query has answered.</summary>
    private const string ReactScreenBehindLazyQuery = """
        import { useEffect } from 'react';
        import { useLazyGetMeQuery } from '@/api/generated';

        export function BillingPage() {
          const [getMe, { data }] = useLazyGetMeQuery();
          useEffect(() => {
            void getMe();
          }, [getMe]);
          return (
            <main>
              <h1>Billing</h1>
              {data ? <input type="text" /> : <p>Loading</p>}
            </main>
          );
        }

        """;

    /// <summary>Each router with RTK Query, and a screen that reads its data through <c>currentData</c> or a lazy query.</summary>
    public static TheoryData<string, string> RtkQueryScreens => new()
    {
        { "frontend.routing=react-router", "currentData" },
        { "frontend.routing=react-router", "lazy" },
        { "frontend.routing=tanstack-router", "currentData" },
        { "frontend.routing=tanstack-router", "lazy" },
    };

    [Theory]
    [MemberData(nameof(RtkQueryScreens))]
    public void With_rtk_query_the_axe_check_shows_the_example_through_current_data_and_lazy_queries_too(string routing, string screenKind)
    {
        using var project = new GeneratedProject("saas-standard-react", routing);
        project.AddModule("billing");
        // The screen is the test's, so the module page's sample goes, its tests and data with it (#734).
        project.DeleteSample("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var generated = TestOf(screen);
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");
        var (behindData, hook) = screenKind == "lazy"
            ? (ReactScreenBehindLazyQuery, "useLazyGetMeQuery")
            : (ReactScreenBehindCurrentData, "useGetMeQuery");
        var example = $$"""
            export const examples: Record<string, unknown> = { {{hook}}: { name: 'Ada' } };

            """;

        // The generated client exports lazy hooks only once the project asks codegen for them.
        if (screenKind == "lazy")
        {
            var config = Path.Combine(project.ProjectDirectory, "web", "app", "openapi-config.json");
            var withLazyHooks = File.ReadAllText(config).Replace(
                "\"hooks\": true",
                "\"hooks\": { \"queries\": true, \"lazyQueries\": true, \"mutations\": true }",
                StringComparison.Ordinal);
            withLazyHooks.ShouldContain("lazyQueries");
            File.WriteAllText(config, withLazyHooks);
        }

        project.Write(screen, behindData);
        project.Write(DataOf(screen), example);
        var (populatedExitCode, populatedOutput, populated) = Statuses(project);
        populatedExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label shown only with the {hook} example:\n{populatedOutput}");
        populated.GetValueOrDefault(generated).ShouldBe(AxeChecks(vue: false, "passed", "passed", "failed", "failed"), populatedOutput);

        project.Write(screen, behindData.Replace(UnlabelledField, """<input type="text" aria-label="Invoice number" />""", StringComparison.Ordinal));
        var (labelledExitCode, labelledOutput, labelled) = Statuses(project);
        labelledExitCode.ShouldBe(0, $"pnpm test:\n{labelledOutput}");
        labelled.GetValueOrDefault(generated).ShouldBe(AxeChecks(vue: false, "passed", "passed", "passed", "passed"));
    }

    /// <summary>A form whose field shows unless its mutation is saving: on first render, nothing is.</summary>
    private const string ReactRtkFormBehindSaving = """
        import { useSaveMutation } from '@/api/generated';

        export function BillingPage() {
          const [save, { isLoading }] = useSaveMutation();
          return (
            <main>
              <h1>Billing</h1>
              {isLoading ? <p>Saving...</p> : <input type="text" onChange={() => void save()} />}
            </main>
          );
        }

        """;

    private const string ReactOrvalFormBehindSaving = """
        import { useSave } from '@/api/generated';

        export function BillingPage() {
          const { mutate, isPending } = useSave();
          return (
            <main>
              <h1>Billing</h1>
              {isPending ? <p>Saving...</p> : <input type="text" onChange={() => mutate()} />}
            </main>
          );
        }

        """;

    private const string VueOrvalFormBehindSaving = """
        <script setup lang="ts">
        import { useSave } from '@/api/generated';

        const { mutate, isPending } = useSave();
        </script>

        <template>
          <main>
            <h1>Billing</h1>
            <p v-if="isPending">Saving...</p>
            <input v-else type="text" @input="mutate()" />
          </main>
        </template>

        """;

    /// <summary>Each client the axe check stands in for, by the screen that saves through the generated client's mutation.</summary>
    public static TheoryData<string, string[], string> MutationClients => new()
    {
        { "saas-standard-react", ["frontend.routing=react-router"], "rtk" },
        { "saas-standard-react", ["frontend.routing=tanstack-router"], "rtk" },
        { "saas-standard-react", ["frontend.state=tanstack-query", "frontend.api-client=orval"], "orval-react" },
        { "saas-standard-vue", ["frontend.routing=vue-router"], "orval-vue" },
    };

    [Theory]
    [MemberData(nameof(MutationClients))]
    public void A_mutation_nobody_has_triggered_is_idle_so_the_axe_check_sees_the_form_as_it_first_shows(
        string profile,
        string[] overrides,
        string client)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");
        // The screen is the test's, so the module page's sample goes, its tests and data with it (#734).
        project.DeleteSample("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var generated = TestOf(screen);
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        // The generated client gains a mutation, as it does once the host maps a POST: save, answered with 204.
        var document = Path.Combine(project.ProjectDirectory, "web", "app", "openapi.json");
        var openApi = JsonNode.Parse(File.ReadAllText(document))!;
        openApi["paths"]!["/save"] = JsonNode.Parse(
            """{ "post": { "operationId": "save", "tags": ["Host"], "responses": { "204": { "description": "No Content" } } } }""");
        File.WriteAllText(document, openApi.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var form = client switch
        {
            "rtk" => ReactRtkFormBehindSaving,
            "orval-react" => ReactOrvalFormBehindSaving,
            _ => VueOrvalFormBehindSaving,
        };

        project.Write(screen, form);
        var (violationExitCode, violationOutput, violation) = Statuses(project);
        violationExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label on a form nobody has submitted:\n{violationOutput}");
        violation.GetValueOrDefault(generated).ShouldBe(AxeChecks(client == "orval-vue", "failed", "failed", "skipped", "skipped"), violationOutput);

        project.Write(screen, form.Replace("<input type=\"text\"", "<input type=\"text\" aria-label=\"Invoice number\"", StringComparison.Ordinal)
            .Replace("<input v-else type=\"text\"", "<input v-else type=\"text\" aria-label=\"Invoice number\"", StringComparison.Ordinal));
        var (labelledExitCode, labelledOutput, labelled) = Statuses(project);
        labelledExitCode.ShouldBe(0, $"pnpm test:\n{labelledOutput}");
        labelled.GetValueOrDefault(generated).ShouldBe(AxeChecks(client == "orval-vue", "passed", "passed", "skipped", "skipped"));
    }

    /// <summary>Each router, with the screen's own test skipping its axe check or with it commented out.</summary>
    public static TheoryData<string, string, string> RoutersAndOwnTests => new()
    {
        { "saas-standard-react", "frontend.routing=react-router", "skipped" },
        { "saas-standard-react", "frontend.routing=react-router", "commented-out" },
        { "saas-standard-react", "frontend.routing=tanstack-router", "skipped" },
        { "saas-standard-react", "frontend.routing=tanstack-router", "commented-out" },
        { "saas-standard-vue", "frontend.routing=vue-router", "skipped" },
        { "saas-standard-vue", "frontend.routing=vue-router", "commented-out" },
    };

    [Theory]
    [MemberData(nameof(RoutersAndOwnTests))]
    public void The_generated_axe_check_runs_on_a_real_screen_whatever_its_own_test_does_and_the_web_tests_fail_without_it(
        string profile,
        string routing,
        string ownTest)
    {
        using var project = new GeneratedProject(profile, routing);
        project.AddModule("billing");
        // The screen is the test's, so the module page's sample goes, its tests and data with it (#734).
        project.DeleteSample("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var vue = screen.EndsWith(".vue", StringComparison.Ordinal);
        var generated = TestOf(screen);
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        project.Write(screen, vue ? VueScreen : ReactScreen);
        project.Write(OwnTestOf(screen), ownTest == "skipped" ? (vue ? VueSkippedOwnTest : ReactSkippedOwnTest) : CommentedOutOwnTest);
        var (violationExitCode, violationOutput, violation) = Statuses(project);
        violationExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label:\n{violationOutput}");
        violation.GetValueOrDefault(generated).ShouldBe(AxeChecks(vue, "failed", "failed", "skipped", "skipped"), violationOutput);

        project.Write(screen, (vue ? VueScreen : ReactScreen).Replace(UnlabelledField, """<input type="text" aria-label="Invoice number" />""", StringComparison.Ordinal));
        var (labelledExitCode, labelledOutput, labelled) = Statuses(project);
        labelledExitCode.ShouldBe(0, $"pnpm test:\n{labelledOutput}");
        labelled.GetValueOrDefault(generated).ShouldBe(AxeChecks(vue, "passed", "passed", "skipped", "skipped"));

        File.Delete(Path.Combine(project.ProjectDirectory, generated));
        var (uncheckedExitCode, uncheckedOutput) = Shell.Run(web, "pnpm", "test");
        uncheckedExitCode.ShouldNotBe(0, $"pnpm test passed without {generated}:\n{uncheckedOutput}");
        uncheckedOutput.ShouldContain($"./modules/billing/{Path.GetFileName(screen)}");
    }

    /// <summary>A screen the project wrote itself: its heading is not the module's name, it has no Manage section, and its field has no label.</summary>
    private const string ReactOwnScreen = """
        export function BillingPage() {
          return (
            <main>
              <h1>Invoices</h1>
              <input type="text" />
            </main>
          );
        }

        """;

    private const string VueOwnScreen = """
        <template>
          <main>
            <h1>Invoices</h1>
            <input type="text" />
          </main>
        </template>

        """;

    private const string Roles = "backend.authorization=roles";

    /// <summary>Each router, API client and framework the axe check has a branch for, with and without a permission probe.</summary>
    public static TheoryData<string, string[]> OwnScreens => new()
    {
        { "saas-standard-react", ["frontend.routing=react-router"] },
        { "saas-standard-react", [Roles, "frontend.routing=react-router"] },
        { "saas-standard-react", [Roles, "frontend.routing=tanstack-router"] },
        { "saas-standard-react", [Roles, "frontend.state=tanstack-query", "frontend.api-client=orval"] },
        { "lean-crud", [Roles, "frontend.api-client=openapi-fetch"] },
        { "lean-crud", [Roles, "frontend.api-client=openapi-fetch", "frontend.routing=tanstack-router"] },
        { "saas-standard-vue", ["frontend.routing=vue-router"] },
        { "saas-standard-vue", [Roles, "frontend.routing=vue-router"] },
        { "saas-standard-vue", [Roles, "frontend.state=pinia"] },
    };

    /// <summary>
    /// The axe check asks nothing of a screen but accessibility (#664, owner's decision): a screen the project wrote,
    /// whose heading is not the module's name and which has no Manage section, passes it once its field has a label and
    /// fails it while it has none. Manage, 403 and a failed probe are the page's own generated test's, which a project
    /// that rewrites the page owns, and that test passes on the page as generated.
    /// </summary>
    [Theory]
    [MemberData(nameof(OwnScreens))]
    public void The_axe_check_asks_nothing_of_a_screen_but_accessibility_and_the_pages_own_test_checks_Manage(string profile, string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var vue = screen.EndsWith(".vue", StringComparison.Ordinal);
        var generated = TestOf(screen);
        var probeTest = ProbeTestOf(screen);
        var asGenerated = File.ReadAllText(Path.Combine(project.ProjectDirectory, screen));
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        project.Write(screen, vue ? VueOwnScreen : ReactOwnScreen);
        var (violationExitCode, violationOutput, violation) = Statuses(project);
        violationExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label:\n{violationOutput}");
        violation.GetValueOrDefault(generated).ShouldNotBeNull(violationOutput).First().ShouldBe("failed", violationOutput);

        project.Write(screen, (vue ? VueOwnScreen : ReactOwnScreen).Replace(UnlabelledField, """<input type="text" aria-label="Invoice number" />""", StringComparison.Ordinal));
        var (_, labelledOutput, labelled) = Statuses(project);
        var checks = labelled.GetValueOrDefault(generated).ShouldNotBeNull(labelledOutput);
        checks.First().ShouldBe("passed", labelledOutput);
        checks.ShouldNotContain("failed", labelledOutput);

        project.Write(screen, asGenerated);
        var (exitCode, output, statuses) = Statuses(project);
        exitCode.ShouldBe(0, $"pnpm test:\n{output}");
        statuses.GetValueOrDefault(generated).ShouldNotBeNull(output).ShouldNotContain("failed", output);
        if (overrides.Contains(Roles))
        {
            statuses.GetValueOrDefault(probeTest).ShouldBe(["passed", "passed", "passed"], output);
        }
        else
        {
            File.Exists(Path.Combine(project.ProjectDirectory, probeTest)).ShouldBeFalse();
        }
    }

    /// <summary>A screen that shows its field only to a manager, through the browser sign-in's <c>RequirePermission</c>.</summary>
    private const string ReactScreenForManagers = """
        import { RequirePermission } from '@/auth/RequirePermission';

        export function BillingPage() {
          return (
            <main>
              <h1>Billing</h1>
              <RequirePermission role="manager">
                <input type="text" />
              </RequirePermission>
            </main>
          );
        }

        """;

    private const string VueScreenForManagers = """
        <script setup lang="ts">
        import RequirePermission from '@/auth/RequirePermission.vue';
        </script>

        <template>
          <main>
            <h1>Billing</h1>
            <RequirePermission role="manager">
              <input type="text" />
            </RequirePermission>
          </main>
        </template>

        """;

    /// <summary>Each router, API client, framework and browser sign-in the axe check has a branch for.</summary>
    public static TheoryData<string, string[]> SignedInScreens => new()
    {
        { "saas-standard-react", [Roles, "frontend.routing=react-router"] },
        { "saas-standard-react", [Roles, "frontend.routing=tanstack-router"] },
        { "saas-standard-react", [Roles, "frontend.state=tanstack-query", "frontend.api-client=orval"] },
        { "lean-crud", [Roles, "frontend.api-client=openapi-fetch"] },
        { "lean-crud", [Roles, "frontend.api-client=openapi-fetch", "frontend.routing=tanstack-router"] },
        { "saas-standard-vue", [Roles, "frontend.routing=vue-router"] },
        { "saas-standard-vue", [Roles, "frontend.state=pinia"] },
        { "saas-standard-react", [Roles, "backend.auth=entra-id", "frontend.auth-client=msal"] },
        { "saas-standard-vue", [Roles, "backend.auth=entra-id", "frontend.auth-client=msal"] },
    };

    /// <summary>
    /// With browser sign-in, the axe check renders the screen for a local user who holds the realm's roles (#661), as
    /// <c>src/auth/RequirePermission.test.*</c> signs one in: a screen that shows its field only to a manager renders,
    /// passes once the field has a label, and fails while it has none, because the developer sees it.
    /// </summary>
    [Theory]
    [MemberData(nameof(SignedInScreens))]
    public void With_browser_sign_in_the_axe_check_renders_the_screen_for_the_signed_in_developer(string profile, string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var screen = Screens(project).ShouldHaveSingleItem();
        var vue = screen.EndsWith(".vue", StringComparison.Ordinal);
        var generated = TestOf(screen);
        var forManagers = vue ? VueScreenForManagers : ReactScreenForManagers;
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        project.Write(screen, forManagers.Replace(UnlabelledField, """<input type="text" aria-label="Invoice number" />""", StringComparison.Ordinal));
        var (_, labelledOutput, labelled) = Statuses(project);
        var checks = labelled.GetValueOrDefault(generated).ShouldNotBeNull(labelledOutput);
        checks.First().ShouldBe("passed", labelledOutput);
        checks.ShouldNotContain("failed", labelledOutput);

        project.Write(screen, forManagers);
        var (violationExitCode, violationOutput, violation) = Statuses(project);
        violationExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label shown to a manager:\n{violationOutput}");
        violation.GetValueOrDefault(generated).ShouldNotBeNull(violationOutput).First().ShouldBe("failed", violationOutput);
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "vitest-report.json")).ShouldContain("Form elements must have labels", Case.Sensitive, violationOutput);
    }

    /// <summary>
    /// A screen's axe check's statuses: as it loads and with its example data, light then dark; with React, also with
    /// nothing to show (#734), which a data file without an empty example skips.
    /// </summary>
    private static List<string> AxeChecks(bool vue, params string[] checks) => vue ? [.. checks] : [.. checks, "skipped", "skipped"];

    /// <summary>
    /// Runs the web app's tests and answers their exit code and output, and the statuses vitest reports for each test
    /// file's tests (<c>passed</c>, <c>failed</c>, <c>skipped</c>, ...), by the file's path relative to the project. With
    /// shadcn's dark theme (#693) a screen's axe check runs each check again in the dark theme, so its statuses come in
    /// pairs: as it loads, light then dark, and with its example data, light then dark.
    /// </summary>
    private static (int ExitCode, string Output, Dictionary<string, List<string>> Statuses) Statuses(GeneratedProject project)
    {
        var app = Path.Combine(project.ProjectDirectory, "web", "app");
        var report = Path.Combine(project.ProjectDirectory, "vitest-report.json");
        File.Delete(report);
        var (exitCode, output) = Shell.Run(app, "pnpm", "test", "--reporter=json", $"--outputFile={report}");
        File.Exists(report).ShouldBeTrue($"vitest wrote no report:\n{output}");

        using var json = JsonDocument.Parse(File.ReadAllText(report));
        var statuses = json.RootElement.GetProperty("testResults").EnumerateArray().ToDictionary(
            result => Path.GetRelativePath(project.ProjectDirectory, result.GetProperty("name").GetString()!).Replace('\\', '/'),
            result => result.GetProperty("assertionResults").EnumerateArray().Select(test => test.GetProperty("status").GetString()!).ToList());
        return (exitCode, output, statuses);
    }

    /// <summary>Every module screen of the web app, relative to the project: a <c>*Page.tsx</c> or <c>*Page.vue</c>.</summary>
    private static List<string> Screens(GeneratedProject project)
    {
        var modules = Path.Combine(project.ProjectDirectory, "web", "app", "src", "modules");
        return [.. Directory.EnumerateFiles(modules, "*Page.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith("Page.tsx", StringComparison.Ordinal) || file.EndsWith("Page.vue", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(project.ProjectDirectory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The screens without the generated axe check beside them.</summary>
    private static List<string> UncheckedScreens(GeneratedProject project) =>
        [.. Screens(project).Where(screen =>
        {
            var test = Path.Combine(project.ProjectDirectory, TestOf(screen));
            return !File.Exists(test) || !File.ReadAllText(test).Contains("expect(await axe(container)).toHaveNoViolations()", StringComparison.Ordinal);
        })];

    /// <summary>The axe check the generator writes beside a screen: <c>BillingPage.axe.test.tsx</c>.</summary>
    private static string TestOf(string screen) =>
        screen.EndsWith(".vue", StringComparison.Ordinal) ? screen[..^".vue".Length] + ".axe.test.ts" : screen[..^".tsx".Length] + ".axe.test.tsx";

    /// <summary>The example data the axe check shows a screen with, which the project fills in: <c>BillingPage.data.ts</c>.</summary>
    private static string DataOf(string screen) =>
        screen.EndsWith(".vue", StringComparison.Ordinal) ? screen[..^".vue".Length] + ".data.ts" : screen[..^".tsx".Length] + ".data.ts";

    /// <summary>The page's generated test of its permission probe, which checks Manage: <c>BillingPage.probe.test.tsx</c>.</summary>
    private static string ProbeTestOf(string screen) =>
        screen.EndsWith(".vue", StringComparison.Ordinal) ? screen[..^".vue".Length] + ".probe.test.ts" : screen[..^".tsx".Length] + ".probe.test.tsx";

    /// <summary>The screen's own component test, which the generator leaves to the project: <c>BillingPage.test.tsx</c>.</summary>
    private static string OwnTestOf(string screen) =>
        screen.EndsWith(".vue", StringComparison.Ordinal) ? screen[..^".vue".Length] + ".test.ts" : screen[..^".tsx".Length] + ".test.tsx";
}

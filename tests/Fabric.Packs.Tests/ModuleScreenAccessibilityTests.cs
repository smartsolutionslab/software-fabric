namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.test.extras.accessibility (#604): every module screen of a single-page app, <c>web/app/src/modules/</c>,
/// gets a component test beside it with an axe check, from <c>fabric new</c> and <c>fabric add-module</c> alike, and the
/// generated <c>src/screens.test.ts</c> fails while a module screen has none.
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

    [Theory]
    [MemberData(nameof(Routers))]
    public void The_web_tests_pass_with_a_module_and_fail_naming_its_screen_once_its_axe_check_is_gone(string profile, string routing)
    {
        using var project = new GeneratedProject(profile, routing);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");

        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");
        var (exitCode, output) = Shell.Run(web, "pnpm", "test");
        exitCode.ShouldBe(0, $"pnpm test:\n{output}");

        var screen = Screens(project).ShouldHaveSingleItem();
        File.Delete(Path.Combine(project.ProjectDirectory, TestOf(screen)));

        var (uncheckedExitCode, uncheckedOutput) = Shell.Run(web, "pnpm", "test");
        uncheckedExitCode.ShouldNotBe(0, $"pnpm test passed without {TestOf(screen)}:\n{uncheckedOutput}");
        uncheckedOutput.ShouldContain($"./modules/billing/{Path.GetFileName(screen)}");
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

    /// <summary>The screens without a component test beside them that asserts axe finds no violations.</summary>
    private static List<string> UncheckedScreens(GeneratedProject project) =>
        [.. Screens(project).Where(screen =>
        {
            var test = Path.Combine(project.ProjectDirectory, TestOf(screen));
            return !File.Exists(test) || !File.ReadAllText(test).Contains("expect(await axe(container)).toHaveNoViolations()", StringComparison.Ordinal);
        })];

    private static string TestOf(string screen) =>
        screen.EndsWith(".vue", StringComparison.Ordinal) ? screen[..^".vue".Length] + ".test.ts" : screen[..^".tsx".Length] + ".test.tsx";
}

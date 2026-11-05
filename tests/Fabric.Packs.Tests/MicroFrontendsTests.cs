namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.architecture=micro-frontends with module federation and a single framework: the shell in web/app, one
/// remote per module in web/modules/&lt;module&gt;, created by add-module, and the rules that keep them apart.
/// </summary>
public sealed class MicroFrontendsTests
{
    private const string Profile = "industrial-event-driven-mfe";

    [Fact]
    public void A_micro_frontends_project_generates_from_the_real_packs_alone_and_add_module_creates_the_modules_remote()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");

        foreach (var file in new[] { "package.json", "vite.config.ts", "tsconfig.json", "eslint.config.js", "src/BillingPage.tsx" })
        {
            File.Exists(Path.Combine(project.ProjectDirectory, "web", "modules", "billing", file)).ShouldBeTrue($"web/modules/billing/{file}");
        }

        File.Exists(Path.Combine(project.ProjectDirectory, "web", "app", "federation.ts")).ShouldBeTrue("the shell's federation configuration");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "pnpm-workspace.yaml")).ShouldContain("  - modules/*");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs")).ShouldContain(".WithRemotes(remotes)");

        // The shell's page for the module mounts the remote instead of holding the module's screens.
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "modules", "billing", "BillingPage.tsx"))
            .ShouldContain("<Remote module=\"billing\" />");
    }

    [Fact]
    public void The_shell_and_a_remote_per_module_install_build_lint_and_pass_their_tests_and_format_check()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.AddModule("order-handling");

        RunWeb(project, "install", "build", "lint", "test");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");

        foreach (var module in new[] { "billing", "order-handling" })
        {
            File.Exists(Path.Combine(project.ProjectDirectory, "web", "modules", module, "dist", "remoteEntry.js")).ShouldBeTrue($"{module}'s remote builds its remoteEntry.js");
        }

        // Built, the shell finds each remote where a deployment serves it, next to the shell.
        var shell = string.Concat(Directory.GetFiles(Path.Combine(project.ProjectDirectory, "web", "app", "dist"), "*.js", SearchOption.AllDirectories).Select(File.ReadAllText));
        shell.ShouldContain("/modules/billing/remoteEntry.js");
        shell.ShouldContain("/modules/order-handling/remoteEntry.js");

        // The module's own screens are in its remote, not bundled into the shell.
        var remote = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "modules", "billing", "src", "BillingPage.tsx"));
        remote.ShouldContain("<h1>Billing</h1>");
    }

    [Fact]
    public void The_shell_importing_a_remote_a_remote_importing_another_framework_and_an_unformatted_remote_fail_lint()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        RunWeb(project, "install");

        project.Write("web/app/src/pages/Bundled.tsx", """
            import { BillingPage } from '@shop/module-billing/src/BillingPage';

            export const Bundled = BillingPage;
            """);
        project.Write("web/modules/billing/src/mixed.ts", """
            import { ref } from 'vue';

            export const count = ref(0);
            """);
        project.Write("web/modules/billing/src/unformatted.ts", "export const greeting = \"hello\"\n");

        var (shellExitCode, shellOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "lint");
        shellExitCode.ShouldNotBe(0, shellOutput);
        shellOutput.ShouldContain("Bundled.tsx");
        shellOutput.ShouldContain("The shell loads a module's screens from its remote at run time; it never imports them");

        var (remoteExitCode, remoteOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "modules", "billing"), "pnpm", "lint");
        remoteExitCode.ShouldNotBe(0, remoteOutput);
        remoteOutput.ShouldContain("mixed.ts");
        remoteOutput.ShouldContain("Every remote is written with the shell's framework, react");

        File.Delete(Path.Combine(project.ProjectDirectory, "web", "modules", "billing", "src", "mixed.ts"));
        var (formatExitCode, formatOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "modules", "billing"), "pnpm", "lint");
        formatExitCode.ShouldNotBe(0, formatOutput);
        formatOutput.ShouldContain("src/unformatted.ts");
    }

    [Fact]
    public void A_remote_for_a_framework_or_design_system_the_pack_does_not_build_fails_loudly()
    {
        using var project = new GeneratedProject(
            "saas-standard-vue",
            "frontend.architecture=micro-frontends",
            "frontend.design-system=tokens-and-components");
        project.AddModule("billing");

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "modules", "billing", "vite.config.ts"))
            .ShouldStartWith("throw new Error('The module-federation pack builds React remotes on the tokens-and-components design system only so far; frontend.framework is vue, frontend.design-system is tokens-and-components (docs/adr/0006 of the generator).');");
    }

    private static void RunWeb(GeneratedProject project, params string[] steps)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in steps)
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }
    }
}

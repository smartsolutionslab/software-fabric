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

        // A utility only a remote names: the shell's Tailwind styles the remotes (docs/adr), so its build emits it.
        project.Write("web/modules/billing/src/Notice.tsx", """
            export function Notice() {
              return <p className="decoration-wavy">Due</p>;
            }

            """);

        RunWeb(project, "install", "build", "lint", "test");

        // The boundary gates' own tests, with Windows paths: a gate comparing separators would let every file through.
        foreach (var (package, test) in new[] { ("app", "src/remoteBoundary.test.ts"), ("modules/billing", "src/boundary.test.ts") })
        {
            var (testExitCode, testOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", package), "pnpm", "exec", "vitest", "run", test);
            testExitCode.ShouldBe(0, $"vitest run {test} in web/{package}:\n{testOutput}");
            testOutput.ShouldContain("1 passed");
        }

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

        var styles = string.Concat(Directory.GetFiles(Path.Combine(project.ProjectDirectory, "web", "app", "dist"), "*.css", SearchOption.AllDirectories).Select(File.ReadAllText));
        styles.ShouldContain(".decoration-wavy{");

        // The module's own screens are in its remote, not bundled into the shell.
        var remote = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "modules", "billing", "src", "BillingPage.tsx"));
        remote.ShouldContain("<h1>Billing</h1>");
    }

    [Fact]
    public void The_shell_importing_a_remote_a_remote_importing_another_framework_or_a_banned_library_a_counted_test_and_an_unformatted_remote_fail_lint()
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
        // A framework's subpath, and the libraries the shell's choices ban: a remote holds the module's screens now.
        project.Write("web/modules/billing/src/subpath.ts", """
            import { useState } from 'preact/hooks';

            export const useCount = () => useState(0);
            """);
        project.Write("web/modules/billing/src/libraries.ts", """
            import { Link } from 'react-router-dom';
            import { object } from 'yup';

            export const parts = [Link, object];
            """);
        // A test that waits for a count rather than a condition: React's rule, where the module's tests now live.
        project.Write("web/modules/billing/src/counted.test.ts", """
            import { expect, test } from 'vitest';

            test('a literal count of timer yields', async () => {
              let ready = false;
              setTimeout(() => {
                ready = true;
              }, 0);
              for (let i = 0; i < 10; i += 1) {
                await new Promise((resolve) => setTimeout(resolve, 0));
              }
              expect(ready).toBe(true);
            });

            """);
        project.Write("web/modules/billing/src/unformatted.ts", "export const greeting = \"hello\"\n");

        var (shellExitCode, shellOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "lint");
        shellExitCode.ShouldNotBe(0, shellOutput);
        shellOutput.ShouldContain("Bundled.tsx");
        shellOutput.ShouldContain("The shell loads a module's screens from its remote at run time; it never imports them");

        var (remoteExitCode, remoteOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "modules", "billing"), "pnpm", "lint");
        remoteExitCode.ShouldNotBe(0, remoteOutput);
        remoteOutput.ShouldContain("mixed.ts");
        remoteOutput.ShouldContain("subpath.ts");
        remoteOutput.ShouldContain("'preact/hooks' import is restricted");
        remoteOutput.ShouldContain("'react-router-dom' import is restricted");
        remoteOutput.ShouldContain("'yup' import is restricted");
        remoteOutput.ShouldContain("Every remote is written with the shell's framework, react");
        remoteOutput.ShouldContain("counted.test.ts");
        remoteOutput.ShouldContain("A fixed count of timer yields is not a bound on work that advances in another phase of the event loop");

        foreach (var file in new[] { "mixed.ts", "subpath.ts", "libraries.ts", "counted.test.ts" })
        {
            File.Delete(Path.Combine(project.ProjectDirectory, "web", "modules", "billing", "src", file));
        }

        var (formatExitCode, formatOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "modules", "billing"), "pnpm", "lint");
        formatExitCode.ShouldNotBe(0, formatOutput);
        formatOutput.ShouldContain("src/unformatted.ts");
    }

    [Fact]
    public void The_shell_bundling_a_remotes_file_or_a_remote_bundling_another_remotes_file_fails_the_build_whatever_the_import_path()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.AddModule("order-handling");
        RunWeb(project, "install");

        // Relative paths, which no import pattern can tell from the shell's own src/modules: the build checks the files.
        project.Write("web/app/src/pages/Home.tsx", """
            import BillingPage from '../../../modules/billing/src/BillingPage';

            export function Home() {
              return <BillingPage />;
            }
            """);
        var (shellExitCode, shellOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "build");
        shellExitCode.ShouldNotBe(0, shellOutput);
        shellOutput.ShouldContain("modules/billing/src/BillingPage.tsx");
        shellOutput.ShouldContain("The shell loads a module's screens from its remote at run time; it never bundles them");

        project.Write("web/modules/billing/src/BillingPage.tsx", """
            import OrderHandlingPage from '../../order-handling/src/OrderHandlingPage';

            export default function BillingPage() {
              return <OrderHandlingPage />;
            }
            """);
        var (remoteExitCode, remoteOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "modules", "billing"), "pnpm", "build");
        remoteExitCode.ShouldNotBe(0, remoteOutput);
        remoteOutput.ShouldContain("modules/order-handling/src/OrderHandlingPage.tsx");
        remoteOutput.ShouldContain("A remote bundles only its own files and packages");
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

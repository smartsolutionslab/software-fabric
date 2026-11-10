namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.mfe-technique=web-components (#286): each remote stays in its own framework and defines its page as a custom
/// element with that framework's own wrapper, Vue's <c>defineCustomElement</c> or React's <c>@r2wc/react-to-web-component</c>;
/// the shell loads each remote as a plain script module and mounts its tag (owner decision, 2026-11-09).
/// </summary>
public sealed class WebComponentsMicroFrontendsTests
{
    [Theory]
    [InlineData("react")]
    [InlineData("vue")]
    public void Each_remote_defines_its_page_as_a_custom_element_with_its_frameworks_own_wrapper(string framework)
    {
        using var project = Project(framework, "tokens");
        project.AddModule("order-handling");
        var app = Path.Combine(project.ProjectDirectory, "web", "app");
        var remote = Path.Combine(project.ProjectDirectory, "web", "modules", "order-handling");
        var page = framework == "vue" ? "OrderHandlingPage.vue" : "OrderHandlingPage.tsx";

        File.Exists(Path.Combine(remote, "src", page)).ShouldBeTrue($"the remote's {framework} page");
        var element = File.ReadAllText(Path.Combine(remote, "src", "element.ts"));
        element.ShouldContain("customElements.define('remote-order-handling', ");
        var package = File.ReadAllText(Path.Combine(remote, "package.json"));
        if (framework == "vue")
        {
            element.ShouldContain("defineCustomElement(OrderHandlingPage, { shadowRoot: false })");
            package.ShouldNotContain("r2wc");
            File.Exists(Path.Combine(app, "src", "remotes", "Remote.vue")).ShouldBeTrue("the shell's Vue component that mounts a remote");
        }
        else
        {
            element.ShouldContain("r2wc(OrderHandlingPage)");
            package.ShouldContain("\"@r2wc/react-to-web-component\": \"2.1.1\"");
            File.Exists(Path.Combine(app, "src", "remotes", "Remote.tsx")).ShouldBeTrue("the shell's React component that mounts a remote");
        }

        // A plain script module: the shell needs no federation runtime, and a remote shares nothing with it.
        File.ReadAllText(Path.Combine(app, "remotes.ts")).ShouldContain("/remote.js");
        File.ReadAllText(Path.Combine(app, "package.json")).ShouldNotContain("federation");
        package.ShouldNotContain("federation");
    }

    [Theory]
    [InlineData("react")]
    [InlineData("vue")]
    public void The_shells_loader_and_a_remotes_element_pass_their_own_tests(string framework)
    {
        using var project = Project(framework, "tokens");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        // The loader: one script per remote, read once, a new address after a failure, and a script without the element refused.
        var (loaderExitCode, loaderOutput) = Shell.Run(Path.Combine(web, "app"), "pnpm", "exec", "vitest", "run", "src/remotes/elements.test.ts");
        loaderExitCode.ShouldBe(0, $"vitest run src/remotes/elements.test.ts:\n{loaderOutput}");
        loaderOutput.ShouldContain("4 passed");

        var remoteTest = framework == "vue" ? "src/element.test.ts" : "src/element.test.tsx";
        var (elementExitCode, elementOutput) = Shell.Run(Path.Combine(web, "modules", "billing"), "pnpm", "exec", "vitest", "run", remoteTest);
        elementExitCode.ShouldBe(0, $"vitest run {remoteTest} in web/modules/billing:\n{elementOutput}");
        elementOutput.ShouldContain("2 passed");

        // Under Aspire `pnpm dev` is vite preview, which builds the remote in watch mode first and closes the watcher with it.
        var (serveExitCode, serveOutput) = Shell.Run(Path.Combine(web, "modules", "billing"), "pnpm", "exec", "vitest", "run", "src/serve.test.ts");
        serveExitCode.ShouldBe(0, $"vitest run src/serve.test.ts in web/modules/billing:\n{serveOutput}");
        serveOutput.ShouldContain("2 passed");
    }

    [Theory]
    [InlineData("react")]
    [InlineData("vue")]
    public void A_remote_builds_one_script_module_that_defines_its_element(string framework)
    {
        using var project = Project(framework, "web-components");
        var web = Path.Combine(project.ProjectDirectory, "web");

        // A stylesheet the remote imports: the shell loads only remote.js, so the remote's styles must come with it.
        project.Write("web/modules/billing/src/notice.css", ".notice {\n  letter-spacing: 3px;\n}\n");
        var element = Path.Combine(web, "modules", "billing", "src", "element.ts");
        File.WriteAllText(element, "import './notice.css';\n" + File.ReadAllText(element));

        foreach (var step in new[] { "install", "build" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var dist = Path.Combine(web, "modules", "billing", "dist");
        Directory.GetFiles(dist, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ShouldBe(["remote.js"]);
        var script = File.ReadAllText(Path.Combine(dist, "remote.js"));
        script.ShouldContain("remote-billing");
        script.ShouldContain(".notice{letter-spacing:3px}");

        // The design system's elements are the shell's: the remote renders <ds-badge> without bundling a second definition.
        script.ShouldContain("ds-badge");
        script.ShouldNotContain("customElements.define(\"ds-badge\"");
        script.ShouldNotContain("customElements.define('ds-badge'");
    }

    [Theory]
    [InlineData("react")]
    [InlineData("vue")]
    public void With_web_components_a_remote_importing_the_design_systems_package_fails_lint_but_its_types_do_not(string framework)
    {
        using var project = Project(framework, "web-components");
        var remote = Path.Combine(project.ProjectDirectory, "web", "modules", "billing");
        var (installExitCode, installOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        // The page renders the shell's elements, and its tests define them as the shell's do.
        var page = File.ReadAllText(Path.Combine(remote, "src", framework == "vue" ? "BillingPage.vue" : "BillingPage.tsx"));
        page.ShouldContain("<ds-badge>Remote</ds-badge>");
        page.ShouldNotContain("@shop/ui");
        File.ReadAllText(Path.Combine(remote, "src", "test-setup.ts")).ShouldContain("import '@shop/ui';");

        project.Write("web/modules/billing/src/defines.ts", """
            import { Badge } from '@shop/ui';

            export const badge = Badge;

            """);
        project.Write("web/modules/billing/src/types.ts", """
            import type { BadgeTone } from '@shop/ui';

            export const tone: BadgeTone = 'primary';

            """);

        var (exitCode, output) = Shell.Run(remote, "pnpm", "lint");
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("defines.ts");
        output.ShouldContain("The shell defines the design system's elements once for the page");
        output.ShouldNotContain("types.ts");

        File.Delete(Path.Combine(remote, "src", "defines.ts"));
        var (typesExitCode, typesOutput) = Shell.Run(remote, "pnpm", "lint");
        typesExitCode.ShouldBe(0, typesOutput);
    }

    private static GeneratedProject Project(string framework, string designSystem)
    {
        var project = new GeneratedProject(
            $"saas-standard-{framework}",
            "frontend.architecture=micro-frontends",
            $"frontend.design-system={designSystem}",
            "frontend.mfe-technique=web-components");
        project.AddModule("billing");
        return project;
    }
}

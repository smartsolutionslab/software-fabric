namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.architecture=micro-frontends on the design systems besides tokens-and-components (#400): the shell and each
/// remote use what the chosen design system provides. With tokens there is no ui package, so nothing names one; with
/// web-components a remote's page shows the design system's custom element, and native federation shares its ui package
/// as it does tokens-and-components'. Vue, which allows tokens and web-components, has native federation's Vue shell
/// runtime and Vue remotes.
/// </summary>
public sealed class MicroFrontendsDesignSystemTests
{
    [Theory]
    [InlineData("module-federation", "tokens", "react")]
    [InlineData("module-federation", "web-components", "react")]
    [InlineData("native-federation", "tokens", "react")]
    [InlineData("native-federation", "web-components", "react")]
    [InlineData("native-federation", "tokens-and-components", "react")]
    [InlineData("native-federation", "tokens", "vue")]
    [InlineData("native-federation", "web-components", "vue")]
    [InlineData("web-components", "tokens", "react")]
    [InlineData("web-components", "web-components", "react")]
    [InlineData("web-components", "tokens", "vue")]
    [InlineData("web-components", "web-components", "vue")]
    public void The_shell_and_a_remote_install_build_lint_pass_their_tests_and_format_check(string technique, string designSystem, string framework)
    {
        using var project = Project(technique, designSystem, framework);

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        // The generated CI's check of the shell's formatting, which runs only in web/app.
        var (formatExitCode, formatOutput) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        var remoteEntry = technique switch
        {
            "native-federation" => "remoteEntry.json",
            "web-components" => "remote.js",
            _ => "remoteEntry.js",
        };
        File.Exists(Path.Combine(web, "modules", "billing", "dist", remoteEntry)).ShouldBeTrue($"billing's remote builds its {remoteEntry}");

        if (technique == "native-federation" && framework == "vue")
        {
            // The remote shares the shell's vue, which its compiled template imports, instead of bundling a copy into the page.
            File.ReadAllText(Path.Combine(web, "modules", "billing", "dist", remoteEntry))
                .ShouldContain("\"packageName\": \"vue\"", customMessage: "billing's remoteEntry.json shares vue");
        }
        else if (technique == "native-federation")
        {
            // The remote shares the shell's react and the JSX runtime its compiled page imports, instead of bundling a
            // second React into the page (#515).
            var entry = File.ReadAllText(Path.Combine(web, "modules", "billing", "dist", remoteEntry));
            entry.ShouldContain("\"packageName\": \"react\"", customMessage: $"billing's remoteEntry.json shares react:\n{entry}");
            entry.ShouldContain("\"packageName\": \"react/jsx-runtime\"", customMessage: $"billing's remoteEntry.json shares react/jsx-runtime:\n{entry}");
        }
    }

    [Theory]
    [InlineData("module-federation")]
    [InlineData("native-federation")]
    [InlineData("web-components")]
    public void With_tokens_no_file_names_a_ui_package_since_none_is_generated(string technique)
    {
        using var project = Project(technique, "tokens");

        Directory.Exists(Path.Combine(project.ProjectDirectory, "web", "packages", "ui")).ShouldBeFalse();
        foreach (var file in Directory.GetFiles(Path.Combine(project.ProjectDirectory, "web"), "*", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).ShouldNotContain("@shop/ui", customMessage: Path.GetRelativePath(project.ProjectDirectory, file));
        }
    }

    [Theory]
    [InlineData("module-federation")]
    [InlineData("native-federation")]
    public void With_web_components_a_remotes_page_shows_the_design_systems_element_not_a_react_component(string technique)
    {
        using var project = Project(technique, "web-components");
        var remote = Path.Combine(project.ProjectDirectory, "web", "modules", "billing");

        var page = File.ReadAllText(Path.Combine(remote, "src", "BillingPage.tsx"));
        page.ShouldContain("import '@shop/ui';");
        page.ShouldContain("<ds-badge>Remote</ds-badge>");
        page.ShouldNotContain("<Badge");

        // React's JSX types know only HTML's tags: the remote declares the design system's, as the shell does.
        File.ReadAllText(Path.Combine(remote, "src", "design-system.d.ts"))
            .ShouldBe(File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "design-system.d.ts")));

        File.ReadAllText(Path.Combine(remote, "src", "BillingPage.test.tsx")).ShouldContain(".shadowRoot");
    }

    [Fact]
    public void With_web_components_a_vue_remotes_page_shows_the_design_systems_element_which_its_compiler_leaves_to_the_browser()
    {
        using var project = Project("native-federation", "web-components", "vue");
        var remote = Path.Combine(project.ProjectDirectory, "web", "modules", "billing");

        var page = File.ReadAllText(Path.Combine(remote, "src", "BillingPage.vue"));
        page.ShouldContain("import '@shop/ui';");
        page.ShouldContain("<ds-badge>Remote</ds-badge>");

        // Vue's compiler resolves an unknown tag as a component unless told: the remote is told as the shell is.
        File.ReadAllText(Path.Combine(remote, "vite.config.ts")).ShouldContain("const customElementPrefixes = ['ds-'];");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "vite.config.ts")).ShouldContain("const customElementPrefixes = ['ds-'];");
        File.Exists(Path.Combine(remote, "src", "design-system.d.ts")).ShouldBeFalse("React's JSX declarations");

        File.ReadAllText(Path.Combine(remote, "src", "BillingPage.test.ts")).ShouldContain(".shadowRoot");
    }

    /// <summary>Native federation shares a package only when its package.json names a version (web.ui-package-fields).</summary>
    [Theory]
    [InlineData("native-federation", true)]
    [InlineData("module-federation", false)]
    public void With_web_components_the_ui_package_names_a_version_only_for_native_federation(string technique, bool versioned)
    {
        using var project = Project(technique, "web-components");

        var package = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "packages", "ui", "package.json"));

        if (versioned)
        {
            package.ShouldContain("""
                  "private": true,
                  "version": "0.0.0",
                  "type": "module",
                """);
        }
        else
        {
            package.ShouldNotContain("\"version\"");
        }
    }

    private static GeneratedProject Project(string technique, string designSystem, string framework = "react")
    {
        var project = new GeneratedProject(
            $"saas-standard-{framework}",
            "frontend.architecture=micro-frontends",
            $"frontend.design-system={designSystem}",
            $"frontend.mfe-technique={technique}");
        project.AddModule("billing");
        return project;
    }
}

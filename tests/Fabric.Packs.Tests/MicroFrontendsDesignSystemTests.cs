namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.architecture=micro-frontends on the design systems besides tokens-and-components (#400): the shell and each
/// remote use what the chosen design system provides. With tokens there is no ui package, so nothing names one; with
/// web-components a remote's page shows the design system's custom element, and native federation shares its ui package
/// as it does tokens-and-components'.
/// </summary>
public sealed class MicroFrontendsDesignSystemTests
{
    [Theory]
    [InlineData("module-federation", "tokens")]
    [InlineData("module-federation", "web-components")]
    [InlineData("native-federation", "tokens")]
    [InlineData("native-federation", "web-components")]
    public void The_shell_and_a_remote_install_build_lint_and_pass_their_tests(string technique, string designSystem)
    {
        using var project = Project(technique, designSystem);

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var remoteEntry = technique == "native-federation" ? "remoteEntry.json" : "remoteEntry.js";
        File.Exists(Path.Combine(web, "modules", "billing", "dist", remoteEntry)).ShouldBeTrue($"billing's remote builds its {remoteEntry}");
    }

    [Theory]
    [InlineData("module-federation")]
    [InlineData("native-federation")]
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

    private static GeneratedProject Project(string technique, string designSystem)
    {
        var project = new GeneratedProject(
            "saas-standard-react",
            "frontend.architecture=micro-frontends",
            $"frontend.design-system={designSystem}",
            $"frontend.mfe-technique={technique}");
        project.AddModule("billing");
        return project;
    }
}

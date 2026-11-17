using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #693: with frontend.ui-library shadcn or shadcn-vue, the theme has a dark block of every variable (shadcn's slate dark
/// values) beside the light one, the app shell a theme toggle (light, dark, system) whose choice is remembered and set
/// before the first paint, and every screen's axe check runs in both themes, while a contrast test reads both palettes.
/// With a design system the tokens own the brand colours and have no dark values, so such a project stays light only;
/// other UI libraries are unchanged.
/// </summary>
public sealed partial class DarkModeTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";

    public static TheoryData<string, string, string> Shells => new()
    {
        { React, "frontend.routing=react-router", "web/app/src/App.tsx" },
        { React, "frontend.routing=tanstack-router", "web/app/src/App.tsx" },
        { Vue, "frontend.routing=vue-router", "web/app/src/App.vue" },
    };

    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_theme_has_a_dark_block_of_every_variable_of_the_light_one_in_shadcns_slate_dark_values(string profile)
    {
        var styles = Files(profile)["web/app/src/index.css"];

        var light = Variables(Block(styles, ":root"));
        var dark = Variables(Block(styles, ".dark"));

        dark.Keys.ShouldBe(light.Keys.Where(name => name != "--radius"), ignoreOrder: true);
        dark["--background"].ShouldBe("oklch(0.129 0.042 264.695)");
        dark["--foreground"].ShouldBe("oklch(0.984 0.003 247.858)");
        dark["--primary"].ShouldBe("oklch(0.929 0.013 255.508)");
        dark["--muted-foreground"].ShouldBe("oklch(0.704 0.04 256.788)");
        dark["--destructive"].ShouldBe("oklch(0.704 0.191 22.216)");
        dark["--border"].ShouldBe("oklch(1 0 0 / 10%)");
        dark["--input"].ShouldBe("oklch(1 0 0 / 15%)");
        dark["--chart-1"].ShouldBe("oklch(0.488 0.243 264.376)");
        dark["--sidebar-primary"].ShouldBe("oklch(0.488 0.243 264.376)");
        dark["--sidebar-border"].ShouldBe("oklch(1 0 0 / 10%)");
        styles.ShouldNotContain("Light only");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_app_shell_has_a_theme_toggle_and_index_html_sets_the_theme_before_the_first_paint(string profile, string routing, string shell)
    {
        var files = Files(profile, routing);

        files[shell].ShouldContain("<ThemeToggle />");
        files.Keys.ShouldContain("web/app/src/theme/theme.ts");
        files.Keys.ShouldContain("web/app/src/theme/theme.test.ts");
        files.Keys.ShouldContain("web/app/src/theme/contrast.test.ts");
        var head = files["web/app/index.html"].Split("</head>")[0];
        head.ShouldContain("<script>");
        head.ShouldContain("localStorage.getItem('theme')");
        head.ShouldContain("(prefers-color-scheme: dark)");
        head.ShouldContain("classList.toggle('dark'");
    }

    [Theory]
    [InlineData(React, "web/app/src/theme/ThemeToggle.tsx", "web/app/src/theme/ThemeToggle.test.tsx", "web/app/src/modules/billing/BillingPage.axe.test.tsx")]
    [InlineData(Vue, "web/app/src/theme/ThemeToggle.vue", "web/app/src/theme/ThemeToggle.test.ts", "web/app/src/modules/billing/BillingPage.axe.test.ts")]
    public void The_toggle_is_tested_and_every_screens_axe_check_runs_in_the_dark_theme_too(string profile, string toggle, string toggleTest, string axeCheck)
    {
        using var project = new GeneratedProject(profile);
        project.AddModule("billing");

        File.Exists(Path.Combine(project.ProjectDirectory, toggle)).ShouldBeTrue(toggle);
        File.Exists(Path.Combine(project.ProjectDirectory, toggleTest)).ShouldBeTrue(toggleTest);
        var check = File.ReadAllText(Path.Combine(project.ProjectDirectory, axeCheck));
        check.ShouldContain("name: 'dark',");
        check.ShouldContain("localStorage.setItem('theme', 'dark');");
        check.ShouldContain("in the $name theme");
    }

    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_agent_rules_and_the_adr_say_how_to_adjust_both_themes(string profile)
    {
        var files = Files(profile);

        var agents = files["AGENTS.md"];
        agents.ShouldNotContain("light only");
        agents.ShouldContain("`:root` (light) and `.dark` (dark)");
        agents.ShouldContain("ThemeToggle");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("ui-library-" + (profile == React ? "shadcn.md" : "shadcn-vue.md"), StringComparison.Ordinal)).Value;
        adr.ShouldContain("`.dark`");
        adr.ShouldContain("prefers-color-scheme");
    }

    [Theory]
    [InlineData(React, "frontend.design-system=tokens")]
    [InlineData(React, "frontend.design-system=tokens-and-components")]
    [InlineData(Vue, "frontend.design-system=tokens")]
    public void With_a_design_systems_tokens_the_theme_stays_light_and_says_why(string profile, string designSystem)
    {
        var files = Files(profile, designSystem);

        files["web/app/src/index.css"].ShouldNotContain(".dark {");
        files["web/app/index.html"].ShouldNotContain("<script>");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/theme/", StringComparison.Ordinal));
        files.Where(file => file.Key.StartsWith("web/app/src/App.", StringComparison.Ordinal)).ShouldAllBe(file => !file.Value.Contains("ThemeToggle"));
        files["AGENTS.md"].ShouldContain("The theme is light only: the design system's tokens set");
    }

    [Theory]
    [InlineData(React, "frontend.ui-library=radix-headless")]
    [InlineData(Vue, "frontend.ui-library=reka-ui")]
    public void Other_ui_libraries_get_no_dark_theme_and_no_toggle(string profile, string library)
    {
        var files = Files(profile, library);

        files["web/app/index.html"].ShouldNotContain("<script>");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/theme/", StringComparison.Ordinal));
        files.Where(file => file.Key.StartsWith("web/app/src/App.", StringComparison.Ordinal)).ShouldAllBe(file => !file.Value.Contains("ThemeToggle"));
    }

    /// <summary>
    /// The web app builds, lints and formats with the toggle in its shell, and its tests check the toggle, the script in
    /// index.html, both palettes' contrast and each screen in both themes: a dark palette with too little contrast fails
    /// them, and so does a screen whose violation shows only in the dark theme.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shells))]
    public void The_web_tests_check_the_toggle_and_fail_on_a_contrast_or_axe_violation_in_the_dark_theme(string profile, string routing, string shell)
    {
        var vue = shell.EndsWith(".vue", StringComparison.Ordinal);
        using var project = new GeneratedProject(profile, routing);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        var toggleTest = vue ? "src/theme/ThemeToggle.test.ts" : "src/theme/ThemeToggle.test.tsx";
        var axeCheck = vue ? "src/modules/billing/BillingPage.axe.test.ts" : "src/modules/billing/BillingPage.axe.test.tsx";
        plain.ShouldContain($"✓ {toggleTest} > with no choice stored the app follows the system's theme, also as it changes", customMessage: testOutput);
        plain.ShouldContain($"✓ {toggleTest} > a choice is shown at once and remembered for the next visit", customMessage: testOutput);
        plain.ShouldContain($"✓ {toggleTest} > choosing system again forgets the choice", customMessage: testOutput);
        plain.ShouldContain($"✓ {toggleTest} > with storage blocked the toggle still renders and switches the theme", customMessage: testOutput);
        plain.ShouldContain("✓ src/theme/theme.test.ts > before the first paint index.html sets the stored dark theme", customMessage: testOutput);
        plain.ShouldContain("✓ src/theme/theme.test.ts > before the first paint index.html follows the system with storage blocked", customMessage: testOutput);
        plain.ShouldContain("✓ src/theme/contrast.test.ts > in the dark theme muted-foreground on background has a contrast of at least 4.5:1", customMessage: testOutput);
        plain.ShouldContain("✓ src/theme/contrast.test.ts > in the light theme primary-foreground on primary has a contrast of at least 4.5:1", customMessage: testOutput);
        plain.ShouldContain($"✓ {axeCheck} > the Billing page has no accessibility violations axe can find in the dark theme", customMessage: testOutput);

        var styles = Path.Combine(app, "src", "index.css");
        var theme = File.ReadAllText(styles);
        var dark = Block(theme, ".dark");
        File.WriteAllText(styles, theme.Replace(dark, dark.Replace("--muted-foreground: oklch(0.704 0.04 256.788);", "--muted-foreground: oklch(0.3 0.04 256.788);", StringComparison.Ordinal), StringComparison.Ordinal));

        var (contrastExitCode, contrastOutput) = Shell.Run(app, "pnpm", "test");

        contrastExitCode.ShouldNotBe(0, $"pnpm test passed with a dark muted-foreground the background hides:\n{contrastOutput}");
        contrastOutput.ShouldContain("in the dark theme muted-foreground on background", Case.Sensitive, contrastOutput);
        File.WriteAllText(styles, theme);

        // A field without a label that the screen shows only in the dark theme.
        if (vue)
        {
            project.Write("web/app/src/modules/billing/BillingPage.vue", """
                <script setup lang="ts">
                const dark = document.documentElement.classList.contains('dark');
                </script>

                <template>
                  <main>
                    <h1>Billing</h1>
                    <input v-if="dark" type="text" />
                  </main>
                </template>

                """);
        }
        else
        {
            project.Write("web/app/src/modules/billing/BillingPage.tsx", """
                export function BillingPage() {
                  const dark = document.documentElement.classList.contains('dark');
                  return (
                    <main>
                      <h1>Billing</h1>
                      {dark && <input type="text" />}
                    </main>
                  );
                }

                """);
        }

        var (darkExitCode, darkOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        darkExitCode.ShouldNotBe(0, $"pnpm test passed with a field without a label shown in the dark theme:\n{darkOutput}");
        var darkPlain = Escapes().Replace(darkOutput, string.Empty);
        darkPlain.ShouldContain($"✓ {axeCheck} > the Billing page has no accessibility violations axe can find", customMessage: darkOutput);
        darkPlain.ShouldContain($"× {axeCheck} > the Billing page has no accessibility violations axe can find in the dark theme", customMessage: darkOutput);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A top-level rule of the stylesheet, from its selector to its closing brace.</summary>
    private static string Block(string styles, string selector)
    {
        var match = new Regex($@"^{Regex.Escape(selector)} \{{[^}}]*\}}", RegexOptions.Multiline).Match(styles);
        match.Success.ShouldBeTrue($"src/index.css has no top-level {selector} block:\n{styles}");
        return match.Value;
    }

    private static Dictionary<string, string> Variables(string block) =>
        Variable().Matches(block).ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value);

    [GeneratedRegex(@"(?<name>--[\w-]+): (?<value>[^;]+);")]
    private static partial Regex Variable();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #356: Reka UI is headless, so it is not tied to Tailwind. With frontend.styling=css-modules its components are styled
/// through a <c>*.module.css</c> beside the component instead of Tailwind classes. css-modules has no pack of its own yet
/// (#270), so it is stood in here.
/// </summary>
public sealed partial class RekaUiCssModulesTests
{
    private const string Profile = "saas-standard-vue";
    private const string Button = "web/app/src/components/ui/BaseButton.vue";
    private const string ButtonStyles = "web/app/src/components/ui/BaseButton.module.css";
    private const string ButtonTest = "web/app/src/components/ui/BaseButton.test.ts";
    private const string Utils = "web/app/src/lib/utils.ts";
    private static readonly string[] CssModules = ["frontend.ui-library=reka-ui", "frontend.styling=css-modules"];

    [Fact]
    public void With_css_modules_the_button_is_styled_by_a_module_beside_it_and_carries_no_tailwind_classes()
    {
        var files = Files(CssModules);

        files.Keys.ShouldContain(ButtonStyles);
        var button = files[Button];
        button.ShouldContain("import styles from './BaseButton.module.css';");
        button.ShouldContain("styles.button");
        button.ShouldContain("variant: { default: styles.variantDefault, outline: styles.variantOutline, ghost: styles.variantGhost }");
        button.ShouldContain("size: { default: styles.sizeDefault, sm: styles.sizeSm, lg: styles.sizeLg }");
        foreach (var tailwind in new[] { "inline-flex", "rounded-md", "bg-slate-900", "border-slate-300", "hover:", "h-9", "px-4", "Tailwind" })
        {
            button.ShouldNotContain(tailwind);
        }

        var styles = files[ButtonStyles];
        foreach (var rule in new[] { ".button {", ".variantDefault {", ".variantOutline {", ".variantGhost {", ".sizeDefault {", ".sizeSm {", ".sizeLg {" })
        {
            styles.ShouldContain(rule);
        }

        var focus = styles[styles.IndexOf(".button:focus-visible {", StringComparison.Ordinal)..];
        focus = focus[..(focus.IndexOf('}', StringComparison.Ordinal) + 1)];
        focus.ShouldContain("outline: 2px solid #64748b;", customMessage: "focus must stay visible whatever the variant's text colour");
        focus.ShouldNotContain("currentColor");
        focus.ShouldNotContain("outline: none");

        styles.ShouldNotContain("@apply");
        styles.ShouldNotContain("@tailwind");
    }

    [Fact]
    public void With_css_modules_the_test_checks_the_module_classes_and_cn_only_joins()
    {
        var files = Files(CssModules);

        var test = files[ButtonTest];
        test.ShouldContain("import styles from './BaseButton.module.css';");
        test.ShouldContain("classList).toContain(styles.variantDefault);");
        test.ShouldContain("classList).toContain(styles.variantOutline);");
        test.ShouldNotContain("slate");

        var utils = files[Utils];
        utils.ShouldContain("return clsx(inputs);");
        utils.ShouldNotContain("tailwind");
        utils.ShouldNotContain("twMerge");

        var package = files["web/app/package.json"];
        package.ShouldContain("\"reka-ui\": \"2.11.0\"");
        package.ShouldContain("\"class-variance-authority\": \"0.7.1\"");
        package.ShouldContain("\"clsx\": \"2.1.1\"");
        package.ShouldNotContain("tailwind-merge");
    }

    [Fact]
    public void With_css_modules_the_rules_and_the_adr_say_css_modules_not_tailwind()
    {
        var files = Files(CssModules);

        var agents = files["AGENTS.md"];
        var section = agents[agents.IndexOf("## UI components: Reka UI", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 1, StringComparison.Ordinal)];
        section.ShouldContain("`BaseButton.module.css`");
        section.ShouldNotContain("Tailwind");

        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-ui-library-reka-ui.md", StringComparison.Ordinal)).Value;
        adr.ShouldStartWith("# UI components: Reka UI with CSS Modules");
        adr.ShouldNotContain("Tailwind");
    }

    [Fact]
    public void With_tailwind_the_button_keeps_its_tailwind_classes_and_has_no_module()
    {
        var files = Files("frontend.ui-library=reka-ui");

        files.Keys.ShouldNotContain(ButtonStyles);
        files[Button].ShouldContain("bg-slate-900 text-white hover:bg-slate-700");
        files[Utils].ShouldContain("twMerge(clsx(inputs))");
        files["web/app/package.json"].ShouldContain("\"tailwind-merge\": \"3.7.0\"");
    }

    [Fact]
    public void With_css_modules_reka_ui_builds_lints_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Profile, CssModules);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/components/ui/BaseButton.test.ts > a button is a button with the default look", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/BaseButton.test.ts > with as-child the child takes the button's look and stays what it is", customMessage: testOutput);
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        using var standIns = new StandIns();
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);
        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

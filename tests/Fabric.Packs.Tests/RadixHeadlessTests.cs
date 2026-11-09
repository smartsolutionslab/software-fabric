using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #271 (M3-49): <c>frontend.ui-library.radix-headless</c>, React's headless option beside shadcn/ui, as Reka UI is Vue's
/// beside shadcn-vue. Components are built in <c>src/components/ui/</c> on Radix primitives, styled with Tailwind's
/// palette (there is no shadcn theme to name) or, with <c>frontend.styling=css-modules</c>, through a
/// <c>*.module.css</c> beside the component (the owner's decision on #270). The button is the first.
/// </summary>
public sealed partial class RadixHeadlessTests
{
    private const string Profile = "saas-standard-react";
    private const string RadixHeadless = "frontend.ui-library=radix-headless";
    private const string Button = "web/app/src/components/ui/button.tsx";
    private const string ButtonStyles = "web/app/src/components/ui/button.module.css";
    private const string ButtonTest = "web/app/src/components/ui/button.test.tsx";
    private const string Utils = "web/app/src/lib/utils.ts";
    private static readonly string[] CssModules = [RadixHeadless, "frontend.styling=css-modules"];

    [Theory]
    [InlineData("tailwind")]
    [InlineData("css-modules")]
    public void A_react_project_with_radix_headless_generates_from_the_real_packs_alone(string styling)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [RadixHeadless, $"frontend.styling={styling}"]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void With_tailwind_the_button_is_a_radix_slot_in_tailwinds_palette_with_no_theme_and_no_module()
    {
        var files = Files(RadixHeadless);

        files.Keys.ShouldNotContain(ButtonStyles);
        files.Keys.ShouldNotContain("web/app/components.json", "shadcn's CLI configuration belongs to shadcn/ui");
        var button = files[Button];
        button.ShouldContain("import { Slot } from '@radix-ui/react-slot';");
        button.ShouldContain("default: 'bg-slate-900 text-white hover:bg-slate-700'");
        button.ShouldContain("outline: 'border border-slate-300 bg-white hover:bg-slate-100'");
        button.ShouldContain("ghost: 'hover:bg-slate-100'");
        button.ShouldNotContain("secondary");
        button.ShouldNotContain("bg-primary");
        files[Utils].ShouldContain("twMerge(clsx(inputs))");

        var stylesheet = files["web/app/src/index.css"];
        stylesheet.ShouldNotContain("--primary", customMessage: "radix-headless brings no theme; its classes name the palette");
        stylesheet.ShouldNotContain("@theme inline");

        var package = files["web/app/package.json"];
        package.ShouldContain("\"@radix-ui/react-slot\": \"1.5.0\"");
        package.ShouldContain("\"class-variance-authority\": \"0.7.1\"");
        package.ShouldContain("\"clsx\": \"2.1.1\"");
        package.ShouldContain("\"tailwind-merge\": \"3.7.0\"");
        package.ShouldNotContain("tw-animate-css");
        package.ShouldNotContain("lucide-react");
    }

    [Fact]
    public void With_css_modules_the_button_is_styled_by_a_module_beside_it_and_carries_no_tailwind_classes()
    {
        var files = Files(CssModules);

        var button = files[Button];
        button.ShouldContain("import styles from './button.module.css';");
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

        var root = styles[..(styles.IndexOf('}', StringComparison.Ordinal) + 1)];
        root.ShouldStartWith(".button {");
        root.ShouldContain("box-sizing: border-box;", customMessage: "a button and an asChild link must size alike without Tailwind's reset");
        var focus = styles[styles.IndexOf(".button:focus-visible {", StringComparison.Ordinal)..];
        focus[..(focus.IndexOf('}', StringComparison.Ordinal) + 1)].ShouldContain("outline: 2px solid #64748b;");
        styles.ShouldNotContain("@apply");

        var test = files[ButtonTest];
        test.ShouldContain("import styles from './button.module.css';");
        test.ShouldContain("classList).toContain(styles.variantDefault);");
        test.ShouldContain("classList).toContain(styles.variantOutline);");
        test.ShouldNotContain("slate");

        files[Utils].ShouldContain("return clsx(inputs);");
        files[Utils].ShouldNotContain("twMerge");
        files["web/app/package.json"].ShouldNotContain("tailwind");
    }

    [Theory]
    [InlineData("tailwind", "# UI components: Radix primitives with Tailwind")]
    [InlineData("css-modules", "# UI components: Radix primitives with CSS Modules")]
    public void The_rules_and_the_adr_say_how_components_are_built_and_styled(string styling, string title)
    {
        var files = Files(RadixHeadless, $"frontend.styling={styling}");

        var agents = files["AGENTS.md"];
        agents.ShouldNotContain("## UI components: shadcn/ui");
        var section = agents[agents.IndexOf("## UI components: Radix primitives", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 1, StringComparison.Ordinal)];
        section.ShouldContain("`button.tsx`");
        section.ShouldContain("lint refuses them");
        (styling == "css-modules" ? section.Contains("`button.module.css`", StringComparison.Ordinal) : section.Contains("Tailwind", StringComparison.Ordinal)).ShouldBeTrue(section);

        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-ui-library-radix-headless.md", StringComparison.Ordinal)).Value;
        adr.ShouldStartWith(title);
        if (styling == "css-modules")
        {
            adr.ShouldNotContain("Tailwind");
        }
    }

    [Fact]
    public void With_tailwind_radix_headless_builds_lints_passes_its_tests_and_refuses_other_component_libraries()
    {
        using var project = new GeneratedProject(Profile, RadixHeadless);

        var (testOutput, lintOutput) = BuildTestAndLintOtherLibraries(project);

        testOutput.ShouldContain("✓ src/components/ui/button.test.tsx > a button is a button with the default look");
        testOutput.ShouldContain("✓ src/components/ui/button.test.tsx > with asChild the child takes the button's look and stays what it is");
        lintOutput.ShouldContain("'@mui/material' import is restricted from being used by a pattern. Use Radix primitives and the components in src/components/ui (docs/adr)");
        lintOutput.ShouldContain("'@headlessui/react' import is restricted from being used by a pattern. Use Radix primitives and the components in src/components/ui (docs/adr)");
    }

    [Fact]
    public void With_css_modules_radix_headless_builds_lints_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Profile, CssModules);

        var (testOutput, lintOutput) = BuildTestAndLintOtherLibraries(project);

        testOutput.ShouldContain("✓ src/components/ui/button.test.tsx > a button is a button with the default look");
        testOutput.ShouldContain("✓ src/components/ui/button.test.tsx > with asChild the child takes the button's look and stays what it is");
        testOutput.ShouldContain("✓ src/cssModules.test.ts");
        lintOutput.ShouldContain("'@mui/material' import is restricted from being used by a pattern. Use Radix primitives and the components in src/components/ui (docs/adr)");
    }

    /// <summary>Installs, builds and tests the web app, then lints it with an import of two other component libraries; returns both outputs.</summary>
    private static (string Test, string Lint) BuildTestAndLintOtherLibraries(GeneratedProject project)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (formatExitCode, format) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);
        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");

        project.Write("web/app/src/pages/OtherLibraries.ts", """
            import { Button } from '@mui/material';
            import { Dialog } from '@headlessui/react';

            export const others = [Button, Dialog];
            """);
        var (lintExitCode, lintOutput) = Shell.Run(web, "pnpm", "lint");
        lintExitCode.ShouldNotBe(0, lintOutput);
        return (Escapes().Replace(testOutput, string.Empty), lintOutput);
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

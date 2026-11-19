using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #698: with frontend.ui-library shadcn or shadcn-vue, the theme has four elevations (--elevation-1…4, layered shadows
/// in the light and the dark palette, the Tailwind utilities shadow-elevation-1…4) that cards, popovers, dialogs and
/// the sidebar use, and a frosted surface (backdrop-blur over a translucent token, --glass and --sidebar-glass) on the
/// header, the sidebar, dialogs and the command palette only: content stays opaque. The contrast test checks text on
/// each translucent token over the worst backdrop, the agent rules say where glass is allowed, and an end-to-end test
/// runs axe's colour-contrast rule on every frosted surface in both themes. Other UI libraries are unchanged.
/// </summary>
public sealed partial class GlassAndElevationTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string Dashboards = "saas-ai-react";
    private const string Contrast = "web/app/src/theme/contrast.test.ts";
    private const string EndToEnd = "web/app/e2e/glass.spec.ts";

    public static TheoryData<string, string, string> Shells => new()
    {
        { React, "frontend.routing=react-router", "web/app/src/App.tsx" },
        { React, "frontend.routing=tanstack-router", "web/app/src/App.tsx" },
        { Vue, "frontend.routing=vue-router", "web/app/src/App.vue" },
    };

    /// <summary>
    /// Per shell: the app shell (whose header is frosted), the sidebar, the dialogs and the command palette, which shows
    /// its dialog's glass through itself. Only these files may carry a frosted surface.
    /// </summary>
    public static TheoryData<string, string, string, string, string[], string> Surfaces => new()
    {
        { React, "frontend.routing=react-router", "web/app/src/App.tsx", "web/app/src/components/ui/sidebar.tsx", ["web/app/src/components/ui/dialog.tsx"], "web/app/src/components/ui/command.tsx" },
        { React, "frontend.routing=tanstack-router", "web/app/src/App.tsx", "web/app/src/components/ui/sidebar.tsx", ["web/app/src/components/ui/dialog.tsx"], "web/app/src/components/ui/command.tsx" },
        { Vue, "frontend.routing=vue-router", "web/app/src/App.vue", "web/app/src/components/ui/sidebar/Sidebar.vue", ["web/app/src/components/ui/dialog/DialogContent.vue", "web/app/src/components/ui/dialog/DialogScrollContent.vue"], "web/app/src/components/ui/command/CommandDialog.vue" },
    };

    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_theme_has_four_elevations_and_translucent_glass_in_both_palettes_mapped_into_tailwind(string profile)
    {
        var styles = Files(profile)["web/app/src/index.css"];

        foreach (var selector in new[] { ":root", ".dark" })
        {
            var palette = Variables(Block(styles, selector));
            for (var level = 1; level <= 4; level++)
            {
                palette.ShouldContainKey($"--elevation-{level}", $"{selector} has no --elevation-{level}");
                palette[$"--elevation-{level}"].Split("),").Length.ShouldBeGreaterThanOrEqualTo(2, $"{selector}'s --elevation-{level} is one shadow, not layered: {palette[$"--elevation-{level}"]}");
            }

            foreach (var glass in new[] { "--glass", "--sidebar-glass" })
            {
                palette.ShouldContainKey(glass, $"{selector} has no {glass}");
                Translucent().IsMatch(palette[glass]).ShouldBeTrue($"{selector}'s {glass} is not a translucent oklch(): {palette[glass]}");
            }
        }

        Variables(Block(styles, ":root"))["--elevation-2"].ShouldNotBe(Variables(Block(styles, ".dark"))["--elevation-2"]);
        var inline = Block(styles, "@theme inline");
        for (var level = 1; level <= 4; level++)
        {
            inline.ShouldContain($"--shadow-elevation-{level}: var(--elevation-{level});");
        }

        inline.ShouldContain("--color-glass: var(--glass);");
        inline.ShouldContain("--color-sidebar-glass: var(--sidebar-glass);");
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Glass_is_on_the_header_the_sidebar_dialogs_and_the_command_palette_only(string profile, string routing, string shell, string sidebar, string[] dialogs, string palette)
    {
        var files = Files(profile, routing);

        var header = HeaderTag().Match(files[shell]);
        header.Success.ShouldBeTrue($"{shell} has no <header>");
        header.Value.ShouldContain("bg-glass");
        header.Value.ShouldContain("backdrop-blur-md");
        files[sidebar].ShouldContain("bg-sidebar-glass");
        files[sidebar].ShouldContain("backdrop-blur-md");
        foreach (var dialog in dialogs)
        {
            files[dialog].ShouldContain("bg-glass", customMessage: dialog);
            files[dialog].ShouldContain("backdrop-blur-md", customMessage: dialog);
        }

        files[palette].ShouldContain("bg-transparent");
        var frosted = files
            .Where(file => file.Key.StartsWith("web/", StringComparison.Ordinal) && !file.Key.EndsWith(".css", StringComparison.Ordinal))
            .Where(file => file.Value.Contains("backdrop-blur", StringComparison.Ordinal) || Glass().IsMatch(file.Value))
            .Select(file => file.Key);
        frosted.ShouldBe([shell, sidebar, .. dialogs], ignoreOrder: true);
    }

    [Theory]
    [InlineData(React, "web/app/src/components/ui/dialog.tsx", "web/app/src/components/ui/sheet.tsx", "web/app/src/components/ui/sidebar.tsx", "web/app/src/components/ui/tooltip.tsx", "web/app/src/components/ui/sonner.tsx", "web/app/src/components/ui/data-table.tsx")]
    [InlineData(Vue, "web/app/src/components/ui/dialog/DialogContent.vue", "web/app/src/components/ui/sheet/SheetContent.vue", "web/app/src/components/ui/sidebar/Sidebar.vue", "web/app/src/components/ui/tooltip/TooltipContent.vue", "web/app/src/components/ui/sonner/Sonner.vue", "web/app/src/components/ui/DataTable.vue")]
    public void Dialogs_popovers_and_the_sidebar_rise_by_the_elevations(string profile, string dialog, string sheet, string sidebar, string tooltip, string toasts, string table)
    {
        var files = Files(profile);

        files[dialog].ShouldContain("shadow-elevation-4");
        files[dialog].ShouldNotContain("shadow-lg");
        files[sheet].ShouldContain("shadow-elevation-4");
        files[sidebar].ShouldContain("shadow-elevation-1");
        files[sidebar].ShouldNotContain("shadow-sm");
        files[tooltip].ShouldContain("shadow-elevation-2");
        files[toasts].ShouldContain("var(--elevation-3)");
        files[table].ShouldContain("shadow-elevation-2");
        files[table].ShouldNotContain("shadow-md");
    }

    [Fact]
    public void Dashboard_widgets_are_cards_on_the_first_elevation()
    {
        var files = Files(Dashboards);

        files["web/app/src/dashboards/PlacedWidget.tsx"].ShouldContain("bg-card p-2 shadow-elevation-1");
        files["web/app/src/dashboards/PendingWidget.tsx"].ShouldContain("bg-card p-2 shadow-elevation-1");
    }

    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_contrast_test_checks_text_on_each_translucent_token_over_the_worst_backdrop(string profile)
    {
        var test = Files(profile)[Contrast];

        test.ShouldContain("['foreground', 'glass'],");
        test.ShouldContain("['muted-foreground', 'glass'],");
        test.ShouldContain("['popover-foreground', 'glass'],");
        test.ShouldContain("['sidebar-foreground', 'sidebar-glass'],");
        test.ShouldContain("over any backdrop");
        test.ShouldContain("const backdrops = [");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void An_end_to_end_test_runs_axe_colour_contrast_on_every_frosted_surface_in_both_themes(string profile, string routing, string shell)
    {
        var files = Files(profile, routing);

        files.Keys.ShouldContain(EndToEnd, shell);
        var spec = files[EndToEnd];
        spec.ShouldContain("import AxeBuilder from '@axe-core/playwright';");
        spec.ShouldContain(".withRules(['color-contrast'])");
        spec.ShouldContain("for (const theme of ['light', 'dark'] as const)");
        spec.ShouldContain("localStorage.setItem('theme'");
        spec.ShouldContain("'header'");
        spec.ShouldContain("[data-slot=\"sidebar-container\"]");
        spec.ShouldContain("ControlOrMeta+K");
        spec.ShouldContain("getByRole('dialog')");
        files["web/app/package.json"].ShouldContain("\"@axe-core/playwright\": \"4.14.0\"");
    }

    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_agent_rules_say_where_glass_is_allowed_and_the_adr_that_screenshot_baselines_go_stale(string profile)
    {
        var files = Files(profile);

        var agents = files["AGENTS.md"];
        agents.ShouldContain("`shadow-elevation-1`");
        agents.ShouldContain("Glass (`backdrop-blur-md` over `bg-glass` or `bg-sidebar-glass`) belongs on the header, the sidebar, dialogs and the command palette only");
        agents.ShouldContain("never on content");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("ui-library-" + (profile == React ? "shadcn.md" : "shadcn-vue.md"), StringComparison.Ordinal)).Value;
        adr.ShouldContain("--elevation-1");
        adr.ShouldContain("worst backdrop");
        adr.ShouldContain("pnpm screenshots:update");
    }

    [Fact]
    public void A_micro_frontends_project_gets_the_tokens_but_no_glass_spec_without_the_shell()
    {
        var files = Files("industrial-event-driven-mfe");

        files.Keys.ShouldNotContain(EndToEnd);
        files.Values.ShouldAllBe(content => !content.Contains("@axe-core/playwright"));
    }

    [Theory]
    [InlineData(React, "frontend.ui-library=radix-headless")]
    [InlineData(Vue, "frontend.ui-library=reka-ui")]
    public void Other_ui_libraries_get_no_glass_and_no_elevations(string profile, string library)
    {
        var files = Files(profile, library);

        files.Keys.ShouldNotContain(EndToEnd);
        files.Where(file => file.Key.StartsWith("web/", StringComparison.Ordinal))
            .ShouldAllBe(file => !file.Value.Contains("backdrop-blur") && !file.Value.Contains("elevation-") && !file.Value.Contains("--glass"));
    }

    /// <summary>
    /// The web app builds, lints and formats with the frosted surfaces and the end-to-end test, its contrast test passes
    /// every glass case in both themes, and a glass token translucent enough to lose AA over a dark backdrop fails it.
    /// </summary>
    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void The_contrast_test_passes_the_glass_and_fails_a_translucent_token_below_aa(string profile)
    {
        using var project = new GeneratedProject(profile);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "vitest", "run", "src/theme/contrast.test.ts", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"the contrast test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        foreach (var palette in new[] { "light", "dark" })
        {
            plain.ShouldContain($"✓ src/theme/contrast.test.ts > in the {palette} theme muted-foreground on glass has a contrast of at least 4.5:1 over any backdrop", customMessage: testOutput);
            plain.ShouldContain($"✓ src/theme/contrast.test.ts > in the {palette} theme sidebar-foreground on sidebar-glass has a contrast of at least 4.5:1 over any backdrop", customMessage: testOutput);
        }

        var styles = Path.Combine(app, "src", "index.css");
        var theme = File.ReadAllText(styles);
        var light = Block(theme, ":root");
        var glass = Variables(light)["--glass"];
        File.WriteAllText(styles, theme.Replace(light, light.Replace($"--glass: {glass};", "--glass: oklch(1 0 0 / 60%);", StringComparison.Ordinal), StringComparison.Ordinal));

        var (failingExitCode, failingOutput) = Shell.Run(app, "pnpm", "vitest", "run", "src/theme/contrast.test.ts", "--reporter=verbose");

        failingExitCode.ShouldNotBe(0, $"the contrast test passed a light glass at 60%, which a dark backdrop shows through:\n{failingOutput}");
        var failing = Escapes().Replace(failingOutput, string.Empty);
        failing.ShouldContain("× src/theme/contrast.test.ts > in the light theme muted-foreground on glass has a contrast of at least 4.5:1 over any backdrop", customMessage: failingOutput);
        failing.ShouldContain("✓ src/theme/contrast.test.ts > in the dark theme muted-foreground on glass has a contrast of at least 4.5:1 over any backdrop", customMessage: failingOutput);
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

    [GeneratedRegex(@"^oklch\([\d.]+ [\d.]+ [\d.]+ / [1-9]\d?%\)$")]
    private static partial Regex Translucent();

    [GeneratedRegex(@"<header [^>]*>")]
    private static partial Regex HeaderTag();

    [GeneratedRegex(@"\bbg-(sidebar-)?glass\b")]
    private static partial Regex Glass();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #271 (M3-49): the React dashboards (user-editable and generative) and the React agent panel build their screens from
/// radix-headless's <c>Button</c> where the project chooses it, with Tailwind and with CSS Modules alike, as #270 did for
/// Reka UI on Vue. The button has no <c>secondary</c> variant, so <c>secondary</c> becomes <c>outline</c>; radix-headless
/// defines none of shadcn/ui's theme colours, so the screens' Tailwind classes name colours of Tailwind's palette
/// instead (the owner accepted both for Reka UI on #270).
/// </summary>
public sealed partial class RadixHeadlessDashboardsTests
{
    private const string Profile = "saas-standard-react";
    private const string RadixHeadless = "frontend.ui-library=radix-headless";
    private static readonly string[] AgentsAndDashboards = ["backend.llm=anthropic", "backend.llm.agents=agent-framework", "frontend.agent-ui=ag-ui", "frontend.dashboards=user-editable,generative"];

    /// <summary>The screens that use a button, and how many of them each one renders.</summary>
    private static readonly Dictionary<string, int> Screens = new()
    {
        ["web/app/src/dashboards/DashboardEditor.tsx"] = 2,
        ["web/app/src/dashboards/DashboardCanvas.tsx"] = 2,
        ["web/app/src/dashboards/Failure.tsx"] = 1,
        ["web/app/src/dashboards/Pager.tsx"] = 2,
        ["web/app/src/dashboards/PlacedWidget.tsx"] = 1,
        ["web/app/src/dashboards/DashboardAssistant.tsx"] = 1,
        ["web/app/src/dashboards/PendingWidget.tsx"] = 2,
        ["web/app/src/agents/AgentPanel.tsx"] = 1,
    };

    /// <summary>shadcn/ui's theme colours; radix-headless projects define none of them.</summary>
    private static readonly string[] ThemeColours = ["primary", "muted", "destructive", "card", "input", "ring", "accent", "background"];

    [Fact]
    public void A_react_project_with_radix_headless_dashboards_and_the_agent_panel_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[]
        {
            [RadixHeadless, .. AgentsAndDashboards],
            [RadixHeadless, .. AgentsAndDashboards, "frontend.validation=valibot"],
            [RadixHeadless, .. AgentsAndDashboards, "frontend.forms=tanstack-form"],
            [RadixHeadless, .. AgentsAndDashboards, "frontend.styling=css-modules"],
            new[] { RadixHeadless, "frontend.dashboards=user-editable" },
        })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }
    }

    [Theory]
    [InlineData("tailwind")]
    [InlineData("css-modules")]
    public void With_radix_headless_the_screens_use_its_button_and_secondary_becomes_outline(string styling)
    {
        var files = Files([RadixHeadless, $"frontend.styling={styling}", .. AgentsAndDashboards]);

        foreach (var (screen, buttons) in Screens)
        {
            var content = files[screen];
            content.ShouldNotStartWith("throw", customMessage: screen);
            content.ShouldContain("import { Button } from '@/components/ui/button';", customMessage: screen);
            content.ShouldNotContain("variant=\"secondary\"", customMessage: screen);
            Regex.Count(content, "<Button\\b").ShouldBe(buttons, screen);
        }

        files["web/app/src/dashboards/DashboardCanvas.tsx"].ShouldContain("<Button type=\"submit\" variant=\"outline\" disabled={loading || full || !roomLeft}>");
        files["web/app/src/dashboards/DashboardAssistant.tsx"].ShouldContain("<Button type=\"submit\" variant=\"outline\" disabled={running}");
    }

    [Fact]
    public void With_radix_headless_and_tailwind_the_screens_name_palette_colours_not_shadcns_theme()
    {
        var files = Files([RadixHeadless, .. AgentsAndDashboards]);

        foreach (var screen in Screens.Keys)
        {
            var classes = string.Join(' ', ClassAttribute().Matches(files[screen]).Select(match => match.Groups["classes"].Value));
            foreach (var colour in ThemeColours)
            {
                Regex.IsMatch(classes, $@"\b(bg|text|border|ring|outline)-{colour}(\b|/)").ShouldBeFalse($"{screen} uses shadcn/ui's {colour}: {classes}");
            }
        }

        files["web/app/src/dashboards/DashboardAssistant.tsx"].ShouldContain("bg-slate-900");
        files["web/app/src/agents/AgentPanel.tsx"].ShouldContain("bg-slate-900");
        files["web/app/src/agents/AgentPanel.tsx"].ShouldContain("text-xs font-medium text-slate-600", customMessage: "slate-500 on the answer's slate-100 is below WCAG AA (#365)");
    }

    [Fact]
    public void With_radix_headless_the_agent_panel_adr_names_its_button()
    {
        var files = Files([RadixHeadless, .. AgentsAndDashboards]);

        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-agent-ui-ag-ui.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("built from the project's Radix `Button` and React Hook Form");
        adr.ShouldNotContain("shadcn/ui");
    }

    [Fact]
    public void With_shadcn_the_screens_keep_its_secondary_button_and_theme()
    {
        var files = Files(AgentsAndDashboards);

        files["web/app/src/dashboards/DashboardCanvas.tsx"].ShouldContain("<Button type=\"submit\" variant=\"secondary\"");
        files["web/app/src/dashboards/DashboardAssistant.tsx"].ShouldContain("<Button type=\"submit\" variant=\"secondary\"");
        files["web/app/src/agents/AgentPanel.tsx"].ShouldContain("bg-primary");
        files["web/app/src/dashboards/PlacedWidget.tsx"].ShouldContain("bg-card");
    }

    [Fact]
    public void Another_react_ui_library_still_fails_the_agent_panel_saying_it_is_not_built_for_it()
    {
        var files = Files(["frontend.ui-library=mantine", "frontend.styling=css-modules", .. AgentsAndDashboards]);

        files["web/app/src/agents/AgentPanel.tsx"].ShouldStartWith("throw new Error('The ag-ui pack builds the agent panel from shadcn/ui, MUI or radix-headless with React Hook Form or TanStack Form and zod or valibot only so far; frontend.ui-library is mantine");
    }

    [Fact]
    public void With_radix_headless_and_tailwind_the_react_web_app_with_dashboards_and_the_agent_panel_builds_lints_formats_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Profile, [RadixHeadless, .. AgentsAndDashboards]);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);
        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.tsx", customMessage: plain);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.tsx", customMessage: plain);
    }

    /// <summary>The project with the overrides; options without a pack (here only mui) are stood in.</summary>
    private static Dictionary<string, string> Files(string[] overrides)
    {
        using var standIns = new StandIns();
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);
        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"(?<![:\w-])className=""(?<classes>[^""]*)""")]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

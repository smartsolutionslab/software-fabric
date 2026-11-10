using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #270 (owner decision, option b): the Vue dashboards (user-editable and generative) and the Vue agent panel build their
/// screens from Reka UI's <c>BaseButton</c> where the project chooses Reka UI, with Tailwind and with CSS Modules alike.
/// <c>BaseButton</c> has no <c>secondary</c> variant, so <c>secondary</c> becomes <c>outline</c>. Reka UI defines none of
/// shadcn-vue's theme colours, so the screens' Tailwind classes name colours of Tailwind's palette instead.
/// </summary>
public sealed partial class RekaUiDashboardsTests
{
    private const string Profile = "saas-standard-vue";
    private const string RekaUi = "frontend.ui-library=reka-ui";
    private const string TanStackForm = "frontend.forms=tanstack-form";
    private static readonly string[] AgentsAndDashboards = ["backend.llm=anthropic", "backend.llm.agents=agent-framework", "frontend.agent-ui=ag-ui", "frontend.dashboards=user-editable,generative"];

    /// <summary>The screens that use a button, and how many of them each one renders.</summary>
    private static readonly Dictionary<string, int> Screens = new()
    {
        ["web/app/src/dashboards/DashboardEditor.vue"] = 2,
        ["web/app/src/dashboards/DashboardCanvas.vue"] = 2,
        ["web/app/src/dashboards/Failure.vue"] = 1,
        ["web/app/src/dashboards/Pager.vue"] = 2,
        ["web/app/src/dashboards/PlacedWidget.vue"] = 1,
        ["web/app/src/dashboards/DashboardAssistant.vue"] = 1,
        ["web/app/src/dashboards/PendingWidget.vue"] = 2,
        ["web/app/src/agents/AgentConversation.vue"] = 1,
    };

    /// <summary>The screens that hold a form.</summary>
    private static readonly string[] Forms =
    [
        "web/app/src/agents/AgentConversation.vue",
        "web/app/src/dashboards/DashboardCanvas.vue",
        "web/app/src/dashboards/DashboardAssistant.vue",
    ];

    /// <summary>shadcn-vue's theme colours; Reka UI's projects define none of them.</summary>
    private static readonly string[] ThemeColours = ["primary", "muted", "destructive", "card", "input", "ring", "accent", "background"];

    [Fact]
    public void A_vue_project_with_reka_ui_dashboards_and_the_agent_panel_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[] { [RekaUi, .. AgentsAndDashboards], [RekaUi, .. AgentsAndDashboards, "frontend.validation=valibot"], new[] { RekaUi, "frontend.dashboards=user-editable" }, [RekaUi, TanStackForm, .. AgentsAndDashboards], [RekaUi, TanStackForm, "frontend.styling=css-modules", .. AgentsAndDashboards] })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }
    }

    [Theory]
    [InlineData("tailwind")]
    [InlineData("css-modules")]
    public void With_reka_ui_the_screens_use_base_button_and_secondary_becomes_outline(string styling)
    {
        var files = Files([RekaUi, $"frontend.styling={styling}", .. AgentsAndDashboards]);

        foreach (var (screen, buttons) in Screens)
        {
            var content = files[screen];
            content.ShouldNotStartWith("<script lang=\"ts\">\nthrow", customMessage: screen);
            content.ShouldContain("import BaseButton from '@/components/ui/BaseButton.vue';", customMessage: screen);
            content.ShouldNotContain("@/components/ui/button", customMessage: screen);
            content.ShouldNotContain("<Button", customMessage: screen);
            content.ShouldNotContain("variant=\"secondary\"", customMessage: screen);
            Regex.Count(content, "<BaseButton\\b").ShouldBe(buttons, screen);
        }

        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("variant=\"outline\" :disabled=\"loading || full || !roomLeft\">Add widget</BaseButton>");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("variant=\"outline\" :disabled=\"running\"");
    }

    [Fact]
    public void With_reka_ui_and_tailwind_the_screens_name_palette_colours_not_shadcn_vues_theme()
    {
        var files = Files([RekaUi, .. AgentsAndDashboards]);

        foreach (var screen in Screens.Keys)
        {
            var classes = string.Join(' ', ClassAttribute().Matches(files[screen]).Select(match => match.Groups["classes"].Value));
            foreach (var colour in ThemeColours)
            {
                Regex.IsMatch(classes, $@"\b(bg|text|border|ring|outline)-{colour}(\b|/)").ShouldBeFalse($"{screen} uses shadcn-vue's {colour}: {classes}");
            }
        }

        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("bg-slate-900");
        files["web/app/src/agents/AgentConversation.vue"].ShouldContain("bg-slate-900");
    }

    [Theory]
    [InlineData("tailwind")]
    [InlineData("css-modules")]
    public void With_reka_ui_and_tanstack_form_the_screens_hold_tanstack_forms_and_base_button(string styling)
    {
        var files = Files([RekaUi, TanStackForm, $"frontend.styling={styling}", .. AgentsAndDashboards]);

        foreach (var (screen, buttons) in Screens)
        {
            var content = files[screen];
            content.ShouldNotStartWith("<script lang=\"ts\">\nthrow", customMessage: screen);
            content.ShouldContain("import BaseButton from '@/components/ui/BaseButton.vue';", customMessage: screen);
            content.ShouldNotContain("<Button", customMessage: screen);
            content.ShouldNotContain("variant=\"secondary\"", customMessage: screen);
            Regex.Count(content, "<BaseButton\\b").ShouldBe(buttons, screen);
        }

        foreach (var screen in Forms)
        {
            files[screen].ShouldContain("import { revalidateLogic, useForm } from '@tanstack/vue-form';", customMessage: screen);
            files[screen].ShouldContain(".Field v-slot=\"{ field", customMessage: screen);
        }

        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("variant=\"outline\" :disabled=\"loading || full || !roomLeft\">Add widget</BaseButton>");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("variant=\"outline\" :disabled=\"running\"");
    }

    [Fact]
    public void With_reka_ui_tanstack_form_and_tailwind_the_form_fields_name_palette_colours_not_shadcn_vues_theme()
    {
        var files = Files([RekaUi, TanStackForm, .. AgentsAndDashboards]);

        foreach (var screen in Forms)
        {
            var classes = string.Join(' ', ClassAttribute().Matches(files[screen]).Select(match => match.Groups["classes"].Value));
            foreach (var colour in ThemeColours)
            {
                Regex.IsMatch(classes, $@"\b(bg|text|border|ring|outline)-{colour}(\b|/)").ShouldBeFalse($"{screen} uses shadcn-vue's {colour}: {classes}");
            }
        }

        files["web/app/src/agents/AgentConversation.vue"].ShouldContain("border-slate-300");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("border-slate-300");
    }

    [Fact]
    public void With_reka_ui_tanstack_form_and_css_modules_the_form_fields_take_their_modules_classes_and_no_tailwind()
    {
        var files = Files([RekaUi, TanStackForm, "frontend.styling=css-modules", .. AgentsAndDashboards]);

        foreach (var screen in Forms)
        {
            ClassAttribute().Matches(files[screen]).Select(match => match.Value).ShouldBeEmpty(screen);
        }

        files["web/app/src/agents/AgentConversation.vue"].ShouldContain(":class=\"styles.input\"");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain(":class=\"styles.input\"");
        Regex.Count(files["web/app/src/dashboards/DashboardCanvas.vue"], ":class=\"styles.input\"").ShouldBe(3);
        Regex.Count(files["web/app/src/dashboards/DashboardCanvas.vue"], ":class=\"styles.field\"").ShouldBe(3);
    }

    [Fact]
    public void With_shadcn_vue_the_screens_keep_its_button_and_theme()
    {
        var files = Files(AgentsAndDashboards);

        foreach (var screen in Screens.Keys)
        {
            files[screen].ShouldContain("import { Button } from '@/components/ui/button';", customMessage: screen);
            files[screen].ShouldNotContain("BaseButton", customMessage: screen);
        }

        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("<Button type=\"submit\" variant=\"secondary\"");
        files["web/app/src/agents/AgentConversation.vue"].ShouldContain("bg-primary");
    }

    [Fact]
    public void Another_vue_ui_library_still_fails_the_screens_saying_they_are_not_built_for_it()
    {
        var files = Files(["frontend.ui-library=vuetify", .. AgentsAndDashboards]);

        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The dashboards pack builds the Vue dashboard page from shadcn-vue or reka-ui with vee-validate, TanStack Form or FormKit, and zod or valibot only so far; frontend.ui-library is vuetify");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The generative dashboards pack builds the Vue assistant from shadcn-vue or reka-ui with vee-validate, TanStack Form or FormKit, and zod or valibot only so far; frontend.ui-library is vuetify");
        files["web/app/src/agents/AgentConversation.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The ag-ui pack builds the Vue agent panel from shadcn-vue or reka-ui with vee-validate, TanStack Form or FormKit, and zod or valibot only so far; frontend.ui-library is vuetify");
    }

    [Fact]
    public void With_reka_ui_and_tailwind_the_vue_web_app_with_dashboards_and_the_agent_panel_builds_lints_formats_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Profile, [RekaUi, .. AgentsAndDashboards]);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);
    }

    [Theory]
    [InlineData("tailwind")]
    [InlineData("css-modules")]
    public void With_reka_ui_and_tanstack_form_the_vue_web_app_with_dashboards_and_the_agent_panel_builds_lints_formats_and_passes_its_tests(string styling)
    {
        using var project = new GeneratedProject(Profile, [RekaUi, TanStackForm, $"frontend.styling={styling}", .. AgentsAndDashboards]);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.ts > an empty message is not sent", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts > a new dashboard gets a fresh time-ordered id and needs a name before it is saved", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts > paging the widget picker clears the choice, so a widget of one page is never added as a visual of another", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.ts > Discard takes the proposal away and leaves the draft as it was", customMessage: testOutput);

        var (formatExitCode, format) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);
    }

    /// <summary>The project with the overrides; options without a pack (here only vuetify) are stood in.</summary>
    private static Dictionary<string, string> Files(string[] overrides)
    {
        using var standIns = new StandIns();
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);
        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"(?<![:\w-])class=""(?<classes>[^""]*)""")]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

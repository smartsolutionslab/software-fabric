using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #270: <c>frontend.styling.css-modules</c>. Components take their classes from a <c>*.module.css</c> beside them
/// instead of Tailwind utilities, and a generated test fails when a class a component takes from its module has no rule
/// with declarations there (Vitest hands out a module's class names whether or not the stylesheet has them, #358). The
/// packs that write classes into their markup whatever the UI library branch for it: the data table, <c>SignedIn</c>,
/// the routing App files, the micro-frontends shell's stylesheet, and the Vue dashboards and agent panel. Vue runs it with
/// Reka UI; React's library with a pack of its own comes with #271, so radix-headless is stood in here.
/// </summary>
public sealed partial class CssModulesTests
{
    private const string Vue = "saas-standard-vue";
    private const string React = "saas-standard-react";
    private const string Check = "web/app/src/cssModules.test.ts";
    private static readonly string[] VueCssModules = ["frontend.ui-library=reka-ui", "frontend.styling=css-modules"];
    private static readonly string[] ReactCssModules = ["frontend.ui-library=radix-headless", "frontend.styling=css-modules"];
    private static readonly string[] AgentsAndDashboards = ["backend.llm=anthropic", "backend.llm.agents=agent-framework", "frontend.agent-ui=ag-ui", "frontend.dashboards=user-editable,generative"];
    private static readonly string[] Msal = ["backend.auth=entra-id", "frontend.auth-client=msal"];

    [Fact]
    public void A_vue_project_with_css_modules_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[] { VueCssModules, [.. VueCssModules, .. AgentsAndDashboards], [.. VueCssModules, .. Msal], [.. VueCssModules, .. AgentsAndDashboards, "frontend.validation=valibot"] })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Vue, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }
    }

    [Fact]
    public void A_css_modules_project_has_no_tailwind_and_a_global_stylesheet_of_its_own()
    {
        foreach (var files in new[] { Files(Vue, VueCssModules), Files(React, ReactCssModules) })
        {
            files["web/app/package.json"].ShouldNotContain("tailwind");
            files["web/app/vite.config.ts"].ShouldNotContain("tailwind");

            var stylesheet = files["web/app/src/index.css"];
            stylesheet.ShouldNotContain("tailwind");
            stylesheet.ShouldContain("box-sizing: border-box;", customMessage: "without Tailwind's reset, padding and borders must not widen boxes");
            stylesheet.ShouldContain("margin: 0;");

            var agents = files["AGENTS.md"];
            agents.ShouldContain("## Styling: CSS Modules");
            agents.ShouldNotContain("## Styling: Tailwind");
            files.Keys.ShouldContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-styling-css-modules.md", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Only_a_css_modules_project_gets_the_stylesheet_check()
    {
        var check = Files(Vue, VueCssModules)[Check];
        check.ShouldContain("from 'node:fs'", customMessage: "the check reads the stylesheets, since Vitest replaces a module's import by a proxy");
        Files(React, ReactCssModules)[Check].ShouldBe(check, "the check knows neither framework");

        Files(Vue).Keys.ShouldNotContain(Check);
        Files(React).Keys.ShouldNotContain(Check);
    }

    [Fact]
    public void Every_vue_component_takes_its_classes_from_a_module_with_a_rule_for_each()
    {
        foreach (var overrides in new string[][] { [.. VueCssModules, .. AgentsAndDashboards], [.. VueCssModules, .. Msal] })
        {
            var files = Files(Vue, overrides);

            var styled = StyledComponents(files);

            styled.ShouldContain("web/app/src/App.vue");
            styled.ShouldContain("web/app/src/auth/SignedIn.vue");
            styled.ShouldContain("web/app/src/components/ui/DataTable.vue");
        }

        var dashboards = StyledComponents(Files(Vue, [.. VueCssModules, .. AgentsAndDashboards]));
        foreach (var component in new[] { "DashboardEditor", "DashboardCanvas", "Failure", "Pager", "PlacedWidget", "DashboardAssistant", "PendingWidget" })
        {
            dashboards.ShouldContain($"web/app/src/dashboards/{component}.vue");
        }

        dashboards.ShouldContain("web/app/src/agents/AgentConversation.vue");
    }

    [Fact]
    public void Every_react_component_takes_its_classes_from_a_module_with_a_rule_for_each()
    {
        foreach (var overrides in new[] { ReactCssModules, [.. ReactCssModules, .. Msal], [.. ReactCssModules, "frontend.routing=tanstack-router"] })
        {
            var styled = StyledComponents(Files(React, overrides));

            styled.ShouldContain("web/app/src/App.tsx");
            styled.ShouldContain("web/app/src/auth/SignedIn.tsx");
            styled.ShouldContain("web/app/src/components/ui/data-table.tsx");
        }
    }

    [Fact]
    public void With_css_modules_the_micro_frontends_shell_does_not_read_the_remotes_for_tailwind()
    {
        // The profile's design system builds Tailwind components only and fails loudly with any other styling (ADR-0006);
        // micro-frontends need one, so the tokens alone stand in for it.
        var files = Files("industrial-event-driven-mfe", [.. ReactCssModules, "frontend.design-system=tokens"]);

        files["web/app/src/index.css"].ShouldNotContain("@source");
        var agents = files["AGENTS.md"];
        var section = agents[agents.IndexOf("## Frontend architecture: micro-frontends", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 1, StringComparison.Ordinal)];
        section.ShouldNotContain("Tailwind");
        section.ShouldContain("CSS module");
        Adr(files, "-frontend-architecture-micro-frontends.md").ShouldNotContain("Tailwind");
    }

    [Fact]
    public void The_conversations_number_no_messages_and_their_answers_name_the_speaker_legibly()
    {
        var files = Files(Vue, [.. VueCssModules, .. AgentsAndDashboards]);

        foreach (var (stylesheet, list) in new[] { ("web/app/src/agents/AgentConversation.module.css", ".messages {"), ("web/app/src/dashboards/DashboardAssistant.module.css", ".conversation {") })
        {
            var css = files[stylesheet];
            var rule = css[css.IndexOf(list, StringComparison.Ordinal)..];
            rule[..(rule.IndexOf('}', StringComparison.Ordinal) + 1)].ShouldContain("list-style: none;", customMessage: $"{stylesheet}: without Tailwind's reset an <ol> numbers its messages");

            var speaker = css[css.IndexOf(".answerSpeaker {", StringComparison.Ordinal)..];
            speaker[..(speaker.IndexOf('}', StringComparison.Ordinal) + 1)].ShouldContain("color: #475569;", customMessage: $"{stylesheet}: slate-500 on the answer's slate-100 is 4.34:1, below WCAG AA");
        }

        var tailwind = Files(Vue, ["frontend.ui-library=reka-ui", .. AgentsAndDashboards]);
        tailwind["web/app/src/agents/AgentConversation.vue"].ShouldContain("class=\"text-xs font-medium text-slate-600\"");
        tailwind["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("class=\"text-xs font-medium text-slate-600\"");
    }

    [Fact]
    public void The_vue_css_modules_web_app_with_dashboards_and_the_agent_panel_builds_lints_formats_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Vue, [.. VueCssModules, .. AgentsAndDashboards]);

        var plain = BuildLintFormatAndTest(project);

        plain.ShouldContain("✓ src/cssModules.test.ts > every class a component takes from its CSS module has a rule with declarations there", customMessage: plain);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.ts", customMessage: plain);
        plain.ShouldContain("✓ src/agents/AgentPanel.test.ts", customMessage: plain);
    }

    [Fact]
    public void The_react_css_modules_web_app_builds_lints_formats_and_passes_its_tests()
    {
        using var project = new GeneratedProject(React, ReactCssModules);

        var plain = BuildLintFormatAndTest(project);

        plain.ShouldContain("✓ src/cssModules.test.ts > every class a component takes from its CSS module has a rule with declarations there", customMessage: plain);
    }

    [Fact]
    public void A_class_whose_rule_is_emptied_or_removed_fails_the_stylesheet_check_naming_the_component_and_the_class()
    {
        using var project = new GeneratedProject(Vue, VueCssModules);
        var buttonStyles = Path.Combine(project.ProjectDirectory, "web/app/src/components/ui/BaseButton.module.css");
        var css = File.ReadAllText(buttonStyles);
        var large = LargeRule().Match(css);
        large.Success.ShouldBeTrue(css);
        File.WriteAllText(buttonStyles, css.Replace(large.Value, ".sizeLg {\n}\n", StringComparison.Ordinal).Replace(".variantGhost:hover {", ".ghostHovered {", StringComparison.Ordinal).Replace(".variantGhost {\n  background-color: transparent;\n}\n", string.Empty, StringComparison.Ordinal));

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);
        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), new Dictionary<string, string> { ["NO_COLOR"] = "1" }, "pnpm", "test", "src/cssModules.test.ts");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("FAIL  src/cssModules.test.ts > every class a component takes from its CSS module has a rule with declarations there", customMessage: output);
        output.ShouldContain("components/ui/BaseButton.vue: styles.sizeLg", customMessage: output);
        output.ShouldContain("components/ui/BaseButton.vue: styles.variantGhost", customMessage: output);
    }

    [Fact]
    public void A_class_its_module_makes_global_fails_the_stylesheet_check()
    {
        using var project = new GeneratedProject(Vue, VueCssModules);
        var buttonStyles = Path.Combine(project.ProjectDirectory, "web/app/src/components/ui/BaseButton.module.css");
        var css = File.ReadAllText(buttonStyles);
        css.ShouldContain(".sizeLg {");
        css.ShouldContain(".sizeSm {");
        File.WriteAllText(buttonStyles, css.Replace(".sizeLg {", ":global(.sizeLg) {", StringComparison.Ordinal).Replace(".sizeSm {", ":global .sizeSm {", StringComparison.Ordinal));

        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, install);
        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), new Dictionary<string, string> { ["NO_COLOR"] = "1" }, "pnpm", "test", "src/cssModules.test.ts");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("components/ui/BaseButton.vue: styles.sizeLg", customMessage: output);
        output.ShouldContain("components/ui/BaseButton.vue: styles.sizeSm", customMessage: output);
    }

    /// <summary>
    /// The web app's components that take classes from a CSS module. Fails for a component with a class attribute of
    /// its own, for a class it takes from a module that has no rule with declarations for it, and for a module with
    /// Tailwind in it.
    /// </summary>
    private static List<string> StyledComponents(Dictionary<string, string> files)
    {
        List<string> styled = [];
        foreach (var (path, content) in files.Where(file => file.Key.StartsWith("web/app/src/", StringComparison.Ordinal) && (file.Key.EndsWith(".vue", StringComparison.Ordinal) || file.Key.EndsWith(".tsx", StringComparison.Ordinal))))
        {
            StaticClass().IsMatch(content).ShouldBeFalse($"{path} has a class attribute of its own:\n{content}");

            var imports = ModuleImport().Matches(content);
            foreach (Match import in imports)
            {
                var name = import.Groups["name"].Value;
                var module = Path.GetFullPath(Path.Combine("/", Path.GetDirectoryName(path)!, import.Groups["module"].Value))[1..];
                files.Keys.ShouldContain(module, $"{path} imports {module}");
                var css = files[module];
                css.ShouldNotContain("@apply");
                css.ShouldNotContain("tailwind");

                var rules = Styled(css);
                var used = Regex.Matches(content, $@"\b{name}\.(?<class>[A-Za-z_][\w]*)").Select(use => use.Groups["class"].Value).Distinct().ToList();
                used.ShouldNotBeEmpty($"{path} imports {module} and uses none of its classes");
                used.Where(use => !rules.Contains(use)).ShouldBeEmpty($"{path} uses classes {module} has no rule with declarations for");
            }

            if (imports.Count > 0)
            {
                styled.Add(path);
            }
        }

        return styled;
    }

    /// <summary>The classes a stylesheet gives at least one declaration, in any selector of a rule.</summary>
    private static HashSet<string> Styled(string css)
    {
        HashSet<string> styled = [];
        foreach (Match rule in Rule().Matches(Comment().Replace(css, string.Empty)))
        {
            if (!Declaration().IsMatch(rule.Groups["body"].Value))
            {
                continue;
            }

            foreach (Match selector in Class().Matches(rule.Groups["selector"].Value))
            {
                styled.Add(selector.Groups["class"].Value);
            }
        }

        return styled;
    }

    private static string BuildLintFormatAndTest(GeneratedProject project)
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
        return Escapes().Replace(testOutput, string.Empty);
    }

    private static string Adr(Dictionary<string, string> files, string suffix) =>
        files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith(suffix, StringComparison.Ordinal)).Value;

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        using var standIns = new StandIns();
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, profile, overrides);
        var generated = Generator.New(Repository.Catalog, packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"(?<![:\w-])(class|className)=""")]
    private static partial Regex StaticClass();

    [GeneratedRegex(@"import (?<name>\w+) from '(?<module>\.{1,2}/[^']+\.module\.css)';")]
    private static partial Regex ModuleImport();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")]
    private static partial Regex Rule();

    [GeneratedRegex(@"[\w-]+\s*:\s*[^;\s]")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"\.(?<class>-?[_a-zA-Z][\w-]*)")]
    private static partial Regex Class();

    [GeneratedRegex(@"\.sizeLg \{[^}]*\}\n")]
    private static partial Regex LargeRule();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

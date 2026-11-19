using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #697: every animation a generated web app draws runs only without <c>prefers-reduced-motion: reduce</c>. A Tailwind
/// animation or transition class carries <c>motion-safe:</c>, and with Tailwind lint fails a class that does not, in a
/// string of a script or in a Vue template; a CSS module's transition sits inside
/// <c>@media (prefers-reduced-motion: no-preference)</c>.
/// </summary>
public sealed partial class ReducedMotionTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string Rule = "motion/respect-reduced-motion";

    private static readonly string[] VueAgentAndDashboards =
    [
        "backend.llm=openai",
        "backend.llm.agents=agent-framework",
        "frontend.agent-ui=ag-ui",
        "frontend.dashboards=user-editable,generative",
    ];

    /// <summary>Every pack whose templates draw an animation or a transition class, each in a project that renders it.</summary>
    public static TheoryData<string, string[]> Animated => new()
    {
        { React, [] },
        { "saas-ai-react", [] },
        { "lean-crud", [] },
        { React, ["frontend.routing=tanstack-router"] },
        { React, ["frontend.forms=tanstack-form"] },
        { React, ["frontend.ui-library=radix-headless"] },
        { Vue, [] },
        { Vue, [.. VueAgentAndDashboards] },
        { Vue, ["frontend.forms=formkit"] },
        { Vue, ["frontend.forms=formkit", "frontend.ui-library=reka-ui"] },
        { Vue, ["frontend.forms=tanstack-form", .. VueAgentAndDashboards] },
        { Vue, ["frontend.ui-library=reka-ui"] },
        { "industrial-event-driven-mfe", [] },
    };

    [Theory]
    [MemberData(nameof(Animated))]
    public void Every_animation_and_transition_class_a_web_app_is_generated_with_runs_only_without_reduced_motion(string profile, string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");

        List<string> unguarded = [];
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var file in Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories).Where(Script))
        {
            unguarded.AddRange(UnguardedClass().Matches(File.ReadAllText(file)).Select(match => $"{Path.GetRelativePath(web, file)}: {match.Groups["class"].Value}"));
        }

        unguarded.ShouldBeEmpty(string.Join('\n', unguarded));
    }

    [Theory]
    [InlineData(React, "frontend.ui-library=radix-headless", "web/app/src/components/ui/button.module.css")]
    [InlineData(Vue, "frontend.ui-library=reka-ui", "web/app/src/components/ui/BaseButton.module.css")]
    public void A_css_modules_button_changes_colour_in_a_transition_only_without_reduced_motion(string profile, string library, string stylesheet)
    {
        var files = Files(profile, library, "frontend.styling=css-modules");

        var css = files[stylesheet];
        css.ShouldContain(
            """
            @media (prefers-reduced-motion: no-preference) {
              .button {
                transition:
            """,
            Case.Sensitive,
            css);
        css.Split("@media (prefers-reduced-motion: no-preference)")[0].ShouldNotContain("transition", customMessage: css);
    }

    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    [InlineData("industrial-event-driven-mfe")]
    public void With_tailwind_lint_has_the_rule_that_fails_an_unguarded_animation_class(string profile)
    {
        var files = Files(profile);

        var lint = files["web/app/eslint.config.js"];
        lint.ShouldContain($"'{Rule}': 'error'");
        files["AGENTS.md"].ShouldContain("motion-safe:");
    }

    [Theory]
    [InlineData(React, "frontend.ui-library=mui")]
    [InlineData(Vue, "frontend.ui-library=reka-ui")]
    public void Without_tailwind_lint_has_no_such_rule(string profile, string library)
    {
        var files = Files(profile, library, "frontend.styling=css-modules");

        files["web/app/eslint.config.js"].ShouldNotContain(Rule);
    }

    /// <summary>
    /// The gate seen working: an animation class without <c>motion-safe:</c>, whether in a script's string or a Vue
    /// template, fails <c>pnpm lint</c> naming it and the rule; with the prefix the same files pass.
    /// </summary>
    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    public void Lint_fails_an_animation_class_without_motion_safe_and_passes_it_with(string profile)
    {
        var react = profile == React;
        using var project = new GeneratedProject(profile);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        WriteSpinner(project, react, guard: string.Empty);
        var (failingExitCode, failingOutput) = Shell.Run(web, "pnpm", "lint");

        failingExitCode.ShouldNotBe(0, failingOutput);
        failingOutput.ShouldContain(Rule, Case.Sensitive, failingOutput);
        failingOutput.ShouldContain("'animate-spin'", Case.Sensitive, failingOutput);
        failingOutput.ShouldContain("'transition-opacity'", Case.Sensitive, failingOutput);

        WriteSpinner(project, react, guard: "motion-safe:");
        var (passingExitCode, passingOutput) = Shell.Run(web, "pnpm", "lint");

        passingExitCode.ShouldBe(0, passingOutput);
    }

    private static void WriteSpinner(GeneratedProject project, bool react, string guard)
    {
        project.Write("web/app/src/lib/fade.ts", $"export const fade = 'opacity-0 {guard}transition-opacity duration-200';\n");
        if (react)
        {
            project.Write(
                "web/app/src/components/LoadingSpinner.tsx",
                $$"""
                export function LoadingSpinner() {
                  return <span className="size-4 {{guard}}animate-spin rounded-full border-2" aria-hidden="true" />;
                }

                """);
        }
        else
        {
            project.Write(
                "web/app/src/components/LoadingSpinner.vue",
                $"""
                <template>
                  <span class="size-4 {guard}animate-spin rounded-full border-2" aria-hidden="true" />
                </template>

                """);
        }
    }

    /// <summary>A script or component the app ships; tests and end-to-end specs draw nothing, and lint leaves them out too.</summary>
    private static bool Script(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains(".test.", StringComparison.Ordinal)
        && !path.Contains(".spec.", StringComparison.Ordinal)
        && (path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal) || path.EndsWith(".vue", StringComparison.Ordinal));

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>
    /// A class whose utility animates (<c>animate-*</c> but <c>animate-none</c>, <c>transition</c> and <c>transition-*</c>
    /// but <c>transition-none</c>) and whose variants include neither <c>motion-safe:</c> nor <c>motion-reduce:</c>: the
    /// lint rule's own pattern.
    /// </summary>
    [GeneratedRegex(@"(?:^|[\s'""`])(?<class>(?![^\s'""`]*motion-(?:safe|reduce):)(?:[^\s'""`]*:)?(?:animate-(?!none(?:[\s'""`]|$))[^\s'""`]+|transition(?:-(?!none(?:[\s'""`]|$))[^\s'""`]+)?))(?=[\s'""`]|$)", RegexOptions.Multiline)]
    private static partial Regex UnguardedClass();
}

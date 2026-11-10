using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.design-system=tokens and the other packs' screens (#373): with the tokens chosen, the agent panel, both
/// dashboards, the data table, FormKit's inputs and the radix-headless and Reka UI buttons read their colours from the
/// tokens' semantic names (docs/slots.md, the design-token contract) instead of hex values or Tailwind's palette. A
/// project without the tokens keeps its colours.
/// </summary>
public sealed partial class DesignTokensScreensTests
{
    private const string Tokens = "frontend.design-system=tokens";
    private const string Anthropic = "backend.llm=anthropic";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string AgUi = "frontend.agent-ui=ag-ui";
    private const string Dashboards = "frontend.dashboards=user-editable,generative";
    private const string CssModules = "frontend.styling=css-modules";
    private const string RadixHeadless = "frontend.ui-library=radix-headless";
    private const string RekaUi = "frontend.ui-library=reka-ui";
    private const string FormKit = "frontend.forms=formkit";

    /// <summary>Every combination in which a pack's screen styles its own colours, each with the tokens.</summary>
    public static TheoryData<string, string[]> Screens => new()
    {
        { "saas-standard-react", [RadixHeadless] },
        { "saas-standard-react", [RadixHeadless, CssModules] },
        { "saas-standard-vue", [RekaUi] },
        { "saas-standard-vue", [RekaUi, CssModules] },
        { "saas-standard-vue", [RekaUi, FormKit] },
        { "saas-standard-vue", [RekaUi, FormKit, CssModules] },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void With_the_tokens_no_screen_writes_a_colour_of_its_own(string profile, string[] library)
    {
        var files = Files(profile, [Tokens, Anthropic, AgentFramework, AgUi, Dashboards, .. library]);

        var colours = files
            .Where(file => IsScreen(file.Key))
            .SelectMany(file => file.Value.Split('\n').Select((line, index) => (file.Key, Line: index + 1, Text: line)))
            .Where(line => HexColour().IsMatch(line.Text) || PaletteClass().IsMatch(line.Text))
            .Select(line => $"{line.Key}:{line.Line}: {line.Text.Trim()}")
            .ToList();

        colours.ShouldBeEmpty(string.Join('\n', colours));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Without_the_tokens_the_screens_keep_their_own_colours(string profile, string[] library)
    {
        var files = Files(profile, [Anthropic, AgentFramework, AgUi, Dashboards, .. library]);

        files.Where(file => IsScreen(file.Key))
            .ShouldContain(file => HexColour().IsMatch(file.Value) || PaletteClass().IsMatch(file.Value));
        files.Values.ShouldNotContain(content => content.Contains("--ds-color-", StringComparison.Ordinal));
    }

    [Fact]
    public void The_tokens_name_an_error_colour_and_a_muted_text_colour()
    {
        var colours = Files("saas-standard-vue", Tokens)["web/packages/tokens/src/color.tokens.json"];

        colours.ShouldContain("\"destructive\": { \"$value\": \"{color.danger.600}\" }");
        colours.ShouldContain("\"muted-foreground\": { \"$value\": \"{color.ink.500}\" }");
        colours.ShouldContain("\"600\": { \"$value\": \"#dc2626\" }");
        colours.ShouldContain("\"500\": { \"$value\": \"#64748b\" }");
    }

    [Theory]
    [InlineData("saas-standard-vue", RekaUi, "web/app/src/components/ui/BaseButton.module.css")]
    [InlineData("saas-standard-react", RadixHeadless, "web/app/src/components/ui/button.module.css")]
    public void With_css_modules_the_screens_read_the_tokens_custom_properties(string profile, string library, string button)
    {
        var files = Files(profile, Tokens, Anthropic, AgentFramework, AgUi, Dashboards, CssModules, library);

        files[button].ShouldContain("background-color: var(--ds-color-primary);");
        files[button].ShouldContain("color: var(--ds-color-primary-foreground);");
        files[button].ShouldContain("border-color: var(--ds-color-border);");
        files.Single(file => file.Key.EndsWith("/data-table.module.css", StringComparison.OrdinalIgnoreCase) || file.Key.EndsWith("/DataTable.module.css", StringComparison.Ordinal)).Value
            .ShouldContain("border-bottom: 1px solid var(--ds-color-border);");
        files.Single(file => file.Key.EndsWith("/DashboardAssistant.module.css", StringComparison.Ordinal)).Value
            .ShouldContain("color: var(--ds-color-destructive);");
        files.Single(file => file.Key is "web/app/src/agents/AgentConversation.module.css" or "web/app/src/agents/AgentPanel.module.css").Value
            .ShouldContain("color: var(--ds-color-muted-foreground);");
    }

    [Theory]
    [InlineData("saas-standard-vue", RekaUi)]
    [InlineData("saas-standard-react", RadixHeadless)]
    public void With_tailwind_the_screens_classes_compile_to_the_tokens(string profile, string library)
    {
        using var project = new GeneratedProject(profile, Tokens, Anthropic, AgentFramework, AgUi, Dashboards, library);
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var css = string.Concat(Directory.GetFiles(Path.Combine(web, "app", "dist", "assets"), "*.css").Select(File.ReadAllText));
        css.ShouldContain(".bg-primary{background-color:var(--ds-color-primary)}");
        css.ShouldContain(".text-primary-foreground{color:var(--ds-color-primary-foreground)}");
        css.ShouldContain(".bg-surface{background-color:var(--ds-color-surface)}");
        css.ShouldContain(".border-border{border-color:var(--ds-color-border)}");
        css.ShouldContain(".text-muted-foreground{color:var(--ds-color-muted-foreground)}");
        css.ShouldContain(".text-destructive{color:var(--ds-color-destructive)}");
    }

    /// <summary>The app's own styles and markup; the tokens package is where the colours live.</summary>
    private static bool IsScreen(string path) =>
        path.StartsWith("web/", StringComparison.Ordinal)
        && !path.StartsWith("web/packages/tokens/", StringComparison.Ordinal)
        && (path.EndsWith(".css", StringComparison.Ordinal)
            || path.EndsWith(".vue", StringComparison.Ordinal)
            || path.EndsWith(".tsx", StringComparison.Ordinal)
            || path.EndsWith(".ts", StringComparison.Ordinal));

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?(?:[0-9a-fA-F]{2})?(?![0-9a-zA-Z_-])")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\b(?:bg|text|border|ring|outline|fill|stroke|divide|from|via|to|shadow|caret|decoration)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|white|black)\b")]
    private static partial Regex PaletteClass();
}

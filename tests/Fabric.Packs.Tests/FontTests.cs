using System.Text.RegularExpressions;
using Fabric.Core.Catalog;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #699: frontend.font, inter (the default) or geist, for shadcn and shadcn-vue: a variable font (OFL) installed from
/// npm (Fontsource) and bundled with the app, never loaded from a font host. The theme's font-sans names it (with Geist,
/// font-mono names Geist Mono), the theme carries a type scale (display, title, body, caption), and tables show tabular
/// figures. A generated test fails when index.html or index.css loads anything from another host. Other UI libraries
/// have no font choice and are unchanged.
/// </summary>
public sealed partial class FontTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string FontsourceVersion = "5.3.0";

    public static TheoryData<string, string> FontsAndProfiles => new()
    {
        { "inter", React },
        { "geist", React },
        { "inter", Vue },
        { "geist", Vue },
    };

    [Fact]
    public void The_catalog_offers_inter_and_geist_with_inter_the_default_for_shadcn_and_shadcn_vue_only()
    {
        var dimension = Repository.Catalog.Dimensions["frontend.font"];

        dimension.Options.Keys.ShouldBe(["inter", "geist"]);
        dimension.Default.ShouldBe(["inter"]);
        dimension.Options.Values.ShouldAllBe(option => option.Tier == SupportTier.Experimental);
        Files(React)["fabric.yaml"].ShouldContain("frontend.font: inter");
        Files(Vue)["fabric.yaml"].ShouldContain("frontend.font: inter");
        Files(React, "frontend.font=geist")["fabric.yaml"].ShouldContain("frontend.font: geist");
        Files(React, "frontend.ui-library=radix-headless")["fabric.yaml"].ShouldNotContain("frontend.font");
        Files(Vue, "frontend.ui-library=reka-ui")["fabric.yaml"].ShouldNotContain("frontend.font");
    }

    [Theory]
    [MemberData(nameof(FontsAndProfiles))]
    public void The_font_is_an_npm_dependency_imported_by_the_theme_and_named_first_in_font_sans(string font, string profile)
    {
        var files = Files(profile, $"frontend.font={font}");
        var dependencies = files["web/app/package.json"];
        var styles = files["web/app/src/index.css"];
        var theme = Block(styles, "@theme");

        if (font == "inter")
        {
            dependencies.ShouldContain($"\"@fontsource-variable/inter\": \"{FontsourceVersion}\"");
            dependencies.ShouldNotContain("geist");
            styles.ShouldContain("@import '@fontsource-variable/inter';");
            theme.ShouldContain("--font-sans: 'Inter Variable', ui-sans-serif, system-ui, sans-serif;");
            theme.ShouldContain("--font-mono: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;");
        }
        else
        {
            dependencies.ShouldContain($"\"@fontsource-variable/geist\": \"{FontsourceVersion}\"");
            dependencies.ShouldContain($"\"@fontsource-variable/geist-mono\": \"{FontsourceVersion}\"");
            dependencies.ShouldNotContain("inter\"");
            styles.ShouldContain("@import '@fontsource-variable/geist';");
            styles.ShouldContain("@import '@fontsource-variable/geist-mono';");
            theme.ShouldContain("--font-sans: 'Geist Variable', ui-sans-serif, system-ui, sans-serif;");
            theme.ShouldContain("--font-mono: 'Geist Mono Variable', ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;");
        }

        // The font's imports come before any rule, next to the theme's other imports.
        styles.IndexOf("@import '@fontsource-variable/", StringComparison.Ordinal).ShouldBeLessThan(styles.IndexOf(":root {", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(React)]
    [InlineData(Vue)]
    [InlineData(React, "frontend.design-system=tokens")]
    [InlineData(React, "frontend.design-system=tokens-and-components")]
    [InlineData(Vue, "frontend.design-system=tokens")]
    public void The_theme_has_a_type_scale_of_display_title_body_and_caption_and_tables_show_tabular_figures(string profile, params string[] overrides)
    {
        var styles = Files(profile, overrides)["web/app/src/index.css"];
        var theme = Block(styles, "@theme");

        foreach (var (step, size, lineHeight) in new[] { ("display", "2.25rem", "2.5rem"), ("title", "1.5rem", "2rem"), ("body", "0.875rem", "1.25rem"), ("caption", "0.75rem", "1rem") })
        {
            theme.ShouldContain($"--text-{step}: {size};");
            theme.ShouldContain($"--text-{step}--line-height: {lineHeight};");
        }

        theme.ShouldContain("--text-display--font-weight: 700;");
        theme.ShouldContain("--text-title--font-weight: 600;");
        var layer = styles[styles.IndexOf("@layer base {", StringComparison.Ordinal)..];
        layer.ShouldContain("  h1 {\n    @apply text-title;\n  }");
        layer.ShouldContain("  table,\n  [role='grid'],\n  [role='treegrid'] {\n    font-variant-numeric: tabular-nums;\n  }");
    }

    [Theory]
    [MemberData(nameof(FontsAndProfiles))]
    public void Nothing_loads_from_a_font_host_and_a_test_of_the_app_fails_on_anything_from_another_host(string font, string profile)
    {
        var files = Files(profile, $"frontend.font={font}");

        foreach (var path in new[] { "web/app/index.html", "web/app/src/index.css" })
        {
            files[path].ShouldNotContain("fonts.googleapis.com");
            files[path].ShouldNotContain("fonts.gstatic.com");
            files[path].ShouldNotContain("https://");
        }

        var test = files["web/app/src/theme/font.test.ts"];
        test.ShouldContain("readFileSync('index.html', 'utf8')");
        test.ShouldContain("readFileSync('src/index.css', 'utf8')");
        test.ShouldContain(font == "inter" ? "{ name: '@fontsource-variable/inter', family: 'Inter Variable' }" : "{ name: '@fontsource-variable/geist-mono', family: 'Geist Mono Variable' }");
    }

    [Theory]
    [InlineData(React, "frontend.ui-library=radix-headless")]
    [InlineData(React, "frontend.ui-library=mui", "frontend.styling=library-theme")]
    [InlineData(Vue, "frontend.ui-library=reka-ui")]
    public void Other_ui_libraries_have_no_font_choice_and_load_no_font(string profile, params string[] overrides)
    {
        var files = Files(profile, overrides);

        files["web/app/package.json"].ShouldNotContain("@fontsource");
        files.Keys.ShouldNotContain("web/app/src/theme/font.test.ts");
        files.Keys.ShouldNotContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.Contains("-font-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(React, "inter", "Inter", "shadcn.md")]
    [InlineData(Vue, "geist", "Geist", "shadcn-vue.md")]
    public void The_agent_rules_and_the_adr_name_the_font_the_scale_and_tabular_figures(string profile, string font, string title, string library)
    {
        var files = Files(profile, $"frontend.font={font}");

        var agents = files["AGENTS.md"];
        agents.ShouldContain($"## Font: {title}");
        agents.ShouldContain("`frontend.font`");
        agents.ShouldContain("never from a font host");
        agents.ShouldContain("`text-display`, `text-title`, `text-body`, `text-caption`");
        agents.ShouldContain("`tabular-nums`");
        agents.ShouldContain("`fabric change --set frontend.font=<font>`");
        agents.ShouldNotContain("decision record of its own");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith($"frontend-font-{font}.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("SIL Open Font License");
        adr.ShouldContain("@fontsource-variable/");
        adr.ShouldContain("src/theme/font.test.ts");
        adr.ShouldContain("`fabric change --set frontend.font=<font>`");
        files.Single(file => file.Key.EndsWith("ui-library-" + library, StringComparison.Ordinal)).Value.ShouldContain("`frontend.font`");
    }

    /// <summary>
    /// The web app builds with the font bundled (its files in dist/assets, no URL to another host in the built CSS) and
    /// its tests pass, the font test among them; a Google Fonts import added to index.css fails that test.
    /// </summary>
    [Theory]
    [InlineData(React, "inter", "inter-latin-wght-normal")]
    [InlineData(Vue, "geist", "geist-mono-latin-wght-normal")]
    public void The_app_bundles_the_font_and_its_font_test_fails_on_a_font_hosts_import(string profile, string font, string fontFile)
    {
        using var project = new GeneratedProject(profile, $"frontend.font={font}");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        var assets = Directory.GetFiles(Path.Combine(app, "dist", "assets"));
        assets.ShouldContain(path => Path.GetFileName(path).StartsWith(fontFile, StringComparison.Ordinal) && path.EndsWith(".woff2", StringComparison.Ordinal));
        foreach (var stylesheet in assets.Where(path => path.EndsWith(".css", StringComparison.Ordinal)))
        {
            RemoteUrl().IsMatch(File.ReadAllText(stylesheet)).ShouldBeFalse($"{Path.GetFileName(stylesheet)} loads something from another host");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/theme/font.test.ts > index.css and index.html load nothing from another host", customMessage: testOutput);

        var styles = Path.Combine(app, "src", "index.css");
        File.WriteAllText(styles, "@import url('https://fonts.googleapis.com/css2?family=Inter:wght@100..900&display=swap');\n" + File.ReadAllText(styles));

        var (hostExitCode, hostOutput) = Shell.Run(app, "pnpm", "test");

        hostExitCode.ShouldNotBe(0, $"pnpm test passed with a Google Fonts import in index.css:\n{hostOutput}");
        hostOutput.ShouldContain("index.css and index.html load nothing from another host", Case.Sensitive, hostOutput);
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

    [GeneratedRegex(@"url\(\s*['""]?(?:[a-z][a-z\d+.-]*:)?//", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteUrl();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

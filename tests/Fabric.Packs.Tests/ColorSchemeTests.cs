using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #694: frontend.color-scheme, blue (the default), green or orange, for shadcn and shadcn-vue: shadcn/ui's own themes
/// (apps/v4/registry/themes.ts) on its slate neutrals, in the light and the dark palette. The primary colour, the
/// sidebar's active item, the focus rings and the charts carry the scheme. A design system's tokens take the scheme's
/// brand colours with a dark value for each colour they set, so such a project gets the dark theme and the toggle too,
/// and its contrast test reads the tokens over the palettes. Other UI libraries have no scheme and are unchanged.
/// </summary>
public sealed partial class ColorSchemeTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";

    /// <summary>The text colours the components put on a background, as src/theme/contrast.test.ts checks them.</summary>
    private static readonly (string Text, string Background)[] Pairs =
    [
        ("foreground", "background"),
        ("card-foreground", "card"),
        ("popover-foreground", "popover"),
        ("primary-foreground", "primary"),
        ("secondary-foreground", "secondary"),
        ("accent-foreground", "accent"),
        ("muted-foreground", "background"),
        ("muted-foreground", "card"),
        ("destructive", "background"),
        ("sidebar-foreground", "sidebar"),
        ("sidebar-primary-foreground", "sidebar-primary"),
        ("sidebar-accent-foreground", "sidebar-accent"),
    ];

    public static TheoryData<string, string> SchemesAndProfiles => new()
    {
        { "blue", React },
        { "green", React },
        { "orange", React },
        { "blue", Vue },
        { "green", Vue },
        { "orange", Vue },
    };

    public static TheoryData<string, string, string> SchemesAndDesignSystems
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var scheme in new[] { "blue", "green", "orange" })
            {
                data.Add(React, scheme, "none");
                data.Add(React, scheme, "tokens");
                data.Add(React, scheme, "tokens-and-components");
                data.Add(React, scheme, "web-components");
                data.Add(Vue, scheme, "none");
                data.Add(Vue, scheme, "tokens");
            }

            return data;
        }
    }

    [Fact]
    public void The_catalog_offers_blue_green_and_orange_with_blue_the_default_for_shadcn_and_shadcn_vue_only()
    {
        var dimension = Repository.Catalog.Dimensions["frontend.color-scheme"];

        dimension.Options.Keys.ShouldBe(["blue", "green", "orange"]);
        dimension.Default.ShouldBe(["blue"]);
        Files(React)["fabric.yaml"].ShouldContain("frontend.color-scheme: blue");
        Files(Vue)["fabric.yaml"].ShouldContain("frontend.color-scheme: blue");
        Files(React, "frontend.color-scheme=green")["fabric.yaml"].ShouldContain("frontend.color-scheme: green");
        Files(React, "frontend.ui-library=radix-headless")["fabric.yaml"].ShouldNotContain("color-scheme");
        Files(Vue, "frontend.ui-library=reka-ui")["fabric.yaml"].ShouldNotContain("color-scheme");
    }

    /// <summary>
    /// The values are shadcn/ui's (apps/v4/registry/themes.ts at 968d8cb9): each theme's primary, its foreground and its
    /// charts. Its sidebar-primary (the 600 tone light, the 500 tone dark) has less than 4.5:1 under the light foreground,
    /// so the sidebar's active item takes the theme's primary, and that tone colours the focus rings, which the theme
    /// leaves to the base colour.
    /// </summary>
    [Theory]
    [MemberData(nameof(SchemesAndProfiles))]
    public void Each_scheme_colours_the_primary_the_sidebars_active_item_the_rings_and_the_charts_on_slate_in_both_palettes(string scheme, string profile)
    {
        var styles = Files(profile, $"frontend.color-scheme={scheme}")["web/app/src/index.css"];
        var light = Variables(Block(styles, ":root"));
        var dark = Variables(Block(styles, ".dark"));
        var (lightPrimary, darkPrimary, foreground, lightRing, darkRing, charts) = scheme switch
        {
            "blue" => ("oklch(0.488 0.243 264.376)", "oklch(0.424 0.199 265.638)", "oklch(0.97 0.014 254.604)", "oklch(0.546 0.245 262.881)", "oklch(0.623 0.214 259.815)",
                new[] { "oklch(0.809 0.105 251.813)", "oklch(0.623 0.214 259.815)", "oklch(0.546 0.245 262.881)", "oklch(0.488 0.243 264.376)", "oklch(0.424 0.199 265.638)" }),
            "green" => ("oklch(0.527 0.154 150.069)", "oklch(0.448 0.119 151.328)", "oklch(0.982 0.018 155.826)", "oklch(0.627 0.194 149.214)", "oklch(0.723 0.219 149.579)",
                new[] { "oklch(0.871 0.15 154.449)", "oklch(0.723 0.219 149.579)", "oklch(0.627 0.194 149.214)", "oklch(0.527 0.154 150.069)", "oklch(0.448 0.119 151.328)" }),
            _ => ("oklch(0.553 0.195 38.402)", "oklch(0.47 0.157 37.304)", "oklch(0.98 0.016 73.684)", "oklch(0.646 0.222 41.116)", "oklch(0.705 0.213 47.604)",
                new[] { "oklch(0.837 0.128 66.29)", "oklch(0.705 0.213 47.604)", "oklch(0.646 0.222 41.116)", "oklch(0.553 0.195 38.402)", "oklch(0.47 0.157 37.304)" }),
        };

        foreach (var (palette, primary, ring) in new[] { (light, lightPrimary, lightRing), (dark, darkPrimary, darkRing) })
        {
            palette["--primary"].ShouldBe(primary);
            palette["--primary-foreground"].ShouldBe(foreground);
            palette["--sidebar-primary"].ShouldBe(primary);
            palette["--sidebar-primary-foreground"].ShouldBe(foreground);
            palette["--ring"].ShouldBe(ring);
            palette["--sidebar-ring"].ShouldBe(ring);
            Enumerable.Range(1, 5).Select(index => palette[$"--chart-{index}"]).ShouldBe(charts);
        }

        // The neutrals stay shadcn's slate.
        light["--radius"].ShouldBe("0.625rem");
        light["--background"].ShouldBe("oklch(1 0 0)");
        light["--foreground"].ShouldBe("oklch(0.129 0.042 264.695)");
        light["--secondary"].ShouldBe("oklch(0.968 0.007 247.896)");
        light["--muted-foreground"].ShouldBe("oklch(0.554 0.046 257.417)");
        light["--border"].ShouldBe("oklch(0.929 0.013 255.508)");
        dark["--background"].ShouldBe("oklch(0.129 0.042 264.695)");
        dark["--secondary"].ShouldBe("oklch(0.279 0.041 260.031)");
        dark["--muted-foreground"].ShouldBe("oklch(0.704 0.04 256.788)");
        dark["--border"].ShouldBe("oklch(1 0 0 / 10%)");
        dark.Keys.ShouldBe(light.Keys.Where(name => name != "--radius"), ignoreOrder: true);
        Files(profile, $"frontend.color-scheme={scheme}")["web/app/components.json"].ShouldContain("\"baseColor\": \"slate\"");
    }

    /// <summary>
    /// The colours a page shows: the palette's, or with a design system the tokens' for each colour they set (their
    /// Tailwind theme maps --color-primary to --ds-color-primary), the dark ones from the tokens' dark values.
    /// </summary>
    [Theory]
    [MemberData(nameof(SchemesAndDesignSystems))]
    public void Every_text_colour_has_at_least_4_5_to_1_on_its_background_in_both_themes_with_and_without_a_design_systems_tokens(string profile, string scheme, string designSystem)
    {
        var files = Files(profile, $"frontend.color-scheme={scheme}", $"frontend.design-system={designSystem}");
        var styles = files["web/app/src/index.css"];
        var tokens = designSystem == "none" ? null : Tokens(files);

        foreach (var theme in new[] { "light", "dark" })
        {
            var palette = Variables(Block(styles, theme == "light" ? ":root" : ".dark"));
            string Colour(string name) =>
                tokens?.Resolve(name, theme) ?? palette[$"--{name}"];

            foreach (var (text, background) in Pairs)
            {
                Contrast(Colour(text), Colour(background)).ShouldBeGreaterThanOrEqualTo(4.5, $"{scheme}, {designSystem}: in the {theme} theme {text} ({Colour(text)}) on {background} ({Colour(background)})");
            }
        }
    }

    [Theory]
    [InlineData(React, "tokens", "green")]
    [InlineData(React, "tokens-and-components", "blue")]
    [InlineData(React, "web-components", "orange")]
    [InlineData(Vue, "tokens", "orange")]
    public void With_a_design_system_the_project_has_the_dark_theme_and_the_toggle_and_the_tokens_take_the_schemes_brand(string profile, string designSystem, string scheme)
    {
        var files = Files(profile, $"frontend.design-system={designSystem}", $"frontend.color-scheme={scheme}");

        files["web/app/src/index.css"].ShouldContain("\n.dark {");
        files["web/app/index.html"].Split("</head>")[0].ShouldContain("localStorage.getItem('theme')");
        files.Keys.ShouldContain("web/app/src/theme/theme.ts");
        files.Keys.ShouldContain("web/app/src/theme/contrast.test.ts");
        files.Where(file => file.Key.StartsWith("web/app/src/App.", StringComparison.Ordinal)).ShouldContain(file => file.Value.Contains("ThemeToggle"));
        files["web/app/src/theme/contrast.test.ts"].ShouldContain("../packages/tokens/dist/dark.css");
        files["AGENTS.md"].ShouldNotContain("light only");
        var brand = scheme switch
        {
            "blue" => new[] { "#2b7fff", "#1447e6", "#193cb8" },
            "green" => ["#00c950", "#008236", "#016630"],
            _ => ["#ff6900", "#ca3500", "#9f2d00"],
        };
        var colours = files["web/packages/tokens/src/color.tokens.json"];
        brand.ShouldAllBe(hex => colours.Contains(hex));
        colours.ShouldNotContain("#4f46e5");
        colours.ShouldNotContain("#3730a3");
        files.Keys.ShouldContain("web/packages/tokens/dark/color.tokens.json");
        var build = files["web/packages/tokens/build.mjs"];
        build.ShouldContain("selector: '.dark'");
        build.ShouldContain("destination: 'dark.css'");
    }

    [Theory]
    [InlineData(React, "frontend.ui-library=radix-headless")]
    [InlineData(React, "frontend.ui-library=mui", "frontend.styling=library-theme")]
    [InlineData(Vue, "frontend.ui-library=reka-ui")]
    public void Other_ui_libraries_have_no_scheme_and_their_tokens_keep_one_light_value_each(string profile, params string[] overrides)
    {
        var files = Files(profile, ["frontend.design-system=tokens", .. overrides]);

        files["fabric.yaml"].ShouldNotContain("color-scheme");
        files["web/packages/tokens/src/color.tokens.json"].ShouldContain("\"500\": { \"$value\": \"#4f46e5\" }");
        files["web/packages/tokens/src/color.tokens.json"].ShouldContain("\"700\": { \"$value\": \"#3730a3\" }");
        files.Keys.ShouldNotContain("web/packages/tokens/dark/color.tokens.json");
        files["web/packages/tokens/build.mjs"].ShouldNotContain("dark");
        files.Keys.ShouldNotContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.Contains("color-scheme", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(React, "green", "shadcn.md")]
    [InlineData(Vue, "orange", "shadcn-vue.md")]
    public void The_agent_rules_and_the_adr_name_the_scheme_and_say_not_to_edit_its_palettes_by_hand(string profile, string scheme, string library)
    {
        var files = Files(profile, $"frontend.color-scheme={scheme}");

        var agents = files["AGENTS.md"];
        agents.ShouldContain($"## Colour scheme: {scheme}");
        agents.ShouldContain("`frontend.color-scheme`");
        agents.ShouldContain("never edit a colour of them by hand");
        agents.ShouldContain("`fabric upgrade`");
        agents.ShouldContain("`fabric change --set frontend.color-scheme=<scheme>`");
        agents.ShouldNotContain("decision record of its own");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith($"color-scheme-{scheme}.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("apps/v4/registry/themes.ts");
        adr.ShouldContain("4.5:1");
        adr.ShouldContain("`fabric change --set frontend.color-scheme=<scheme>`");
        var libraryAdr = files.Single(file => file.Key.EndsWith("ui-library-" + library, StringComparison.Ordinal)).Value;
        libraryAdr.ShouldContain("`frontend.color-scheme`");
    }

    /// <summary>
    /// With a design system's tokens the web app builds and its tests pass, the contrast test reading the tokens as built
    /// over the palettes: a dark token the background hides fails it, and every screen's axe check runs in both themes.
    /// </summary>
    [Theory]
    [InlineData(React, "green", "src/modules/billing/BillingPage.axe.test.tsx")]
    [InlineData(Vue, "orange", "src/modules/billing/BillingPage.axe.test.ts")]
    public void With_tokens_the_contrast_test_reads_the_tokens_dark_values_and_fails_on_one_the_background_hides(string profile, string scheme, string axeCheck)
    {
        using var project = new GeneratedProject(profile, "frontend.design-system=tokens", $"frontend.color-scheme={scheme}");
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
        plain.ShouldContain("✓ src/theme/contrast.test.ts > in the dark theme muted-foreground on background has a contrast of at least 4.5:1", customMessage: testOutput);
        plain.ShouldContain("✓ src/theme/contrast.test.ts > in the light theme primary-foreground on primary has a contrast of at least 4.5:1", customMessage: testOutput);
        plain.ShouldContain($"✓ {axeCheck} > the Billing page has no accessibility violations axe can find in the dark theme", customMessage: testOutput);
        File.ReadAllText(Path.Combine(web, "packages", "tokens", "dist", "dark.css")).ShouldContain(".dark {");

        var darkTokens = Path.Combine(web, "packages", "tokens", "dark", "color.tokens.json");
        var original = File.ReadAllText(darkTokens);
        File.WriteAllText(darkTokens, MutedForeground().Replace(original, "\"muted-foreground\": { \"$value\": \"{color.ink.900}\" }"));

        var (contrastExitCode, contrastOutput) = Shell.Run(app, "pnpm", "test");

        contrastExitCode.ShouldNotBe(0, $"pnpm test passed with a dark muted-foreground token the background hides:\n{contrastOutput}");
        contrastOutput.ShouldContain("in the dark theme muted-foreground on background", Case.Sensitive, contrastOutput);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static DesignTokens Tokens(Dictionary<string, string> files)
    {
        files.Keys.ShouldContain("web/packages/tokens/dark/color.tokens.json");
        return new DesignTokens(
            Flatten(files["web/packages/tokens/src/color.tokens.json"]),
            Flatten(files["web/packages/tokens/dark/color.tokens.json"]));
    }

    /// <summary>Every token of a DTCG file by its path (color.brand.700), with its $value as written.</summary>
    private static Dictionary<string, string> Flatten(string json)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(JsonElement element, string path)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "$value")
                {
                    tokens[path] = property.Value.GetString()!;
                }
                else if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    Walk(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}");
                }
            }
        }

        using var document = JsonDocument.Parse(json);
        Walk(document.RootElement, string.Empty);
        return tokens;
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

    private static double Contrast(string first, string second)
    {
        var (lighter, darker) = (Math.Max(Luminance(first), Luminance(second)), Math.Min(Luminance(first), Luminance(second)));
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>The relative luminance (WCAG) of an opaque #rrggbb or oklch() colour, oklch() clipped to the sRGB gamut.</summary>
    private static double Luminance(string colour)
    {
        double red, green, blue;
        var hex = Hex().Match(colour);
        if (hex.Success)
        {
            double Channel(int index)
            {
                var value = int.Parse(hex.Groups[1].Value.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            (red, green, blue) = (Channel(0), Channel(1), Channel(2));
        }
        else
        {
            var oklch = Oklch().Match(colour);
            oklch.Success.ShouldBeTrue($"an opaque #rrggbb or oklch() colour, not {colour}");
            var lightness = double.Parse(oklch.Groups[1].Value, CultureInfo.InvariantCulture);
            var chroma = double.Parse(oklch.Groups[2].Value, CultureInfo.InvariantCulture);
            var hue = double.Parse(oklch.Groups[3].Value, CultureInfo.InvariantCulture) * Math.PI / 180;
            var a = chroma * Math.Cos(hue);
            var b = chroma * Math.Sin(hue);
            var l = Math.Pow(lightness + (0.3963377774 * a) + (0.2158037573 * b), 3);
            var m = Math.Pow(lightness - (0.1055613458 * a) - (0.0638541728 * b), 3);
            var s = Math.Pow(lightness - (0.0894841775 * a) - (1.291485548 * b), 3);
            red = Math.Clamp((4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s), 0, 1);
            green = Math.Clamp((-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s), 0, 1);
            blue = Math.Clamp((-0.0041960863 * l) - (0.7034186147 * m) + (1.707614701 * s), 0, 1);
        }

        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    [GeneratedRegex(@"(?<name>--[\w-]+): (?<value>[^;]+);")]
    private static partial Regex Variable();

    [GeneratedRegex(@"^#([0-9a-fA-F]{6})$")]
    private static partial Regex Hex();

    [GeneratedRegex(@"^oklch\(([\d.]+) ([\d.]+) ([\d.]+)\)$")]
    private static partial Regex Oklch();

    [GeneratedRegex(@"""muted-foreground"": \{ ""\$value"": ""[^""]+"" \}")]
    private static partial Regex MutedForeground();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();

    /// <summary>The tokens' colours: a light token by its name, a dark one from the dark file before the light file.</summary>
    private sealed class DesignTokens(Dictionary<string, string> light, Dictionary<string, string> dark)
    {
        /// <summary>The token's colour for a palette variable (primary is color.primary), or null where no token sets it.</summary>
        public string? Resolve(string name, string theme)
        {
            var path = $"color.{name}";
            var value = theme == "dark" && dark.TryGetValue(path, out var darkValue) ? darkValue : light.GetValueOrDefault(path);
            while (value is not null && value.StartsWith('{'))
            {
                var reference = value.Trim('{', '}');
                value = theme == "dark" && dark.TryGetValue(reference, out var darkReference) ? darkReference : light.GetValueOrDefault(reference);
                value.ShouldNotBeNull($"the token {path} references {reference}, which does not exist");
            }

            return value;
        }
    }
}

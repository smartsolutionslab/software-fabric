using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #700: with shadcn or shadcn-vue, whose theme defines <c>--chart-1…5</c>, every chart pack draws in those colours and
/// follows dark mode (#693). ECharts and Chart.js draw on a canvas, so <c>src/components/chart-theme.ts</c> reads the
/// variables (oklch made rgb, which their colour parsers understand) and reads them again when the theme on
/// <c>&lt;html&gt;</c> changes; Recharts draws SVG, so its colours are the variables themselves. Without those UI
/// libraries there are no such variables, and the charts keep their library's colours.
/// </summary>
public sealed class ChartThemeTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";

    [Theory]
    [InlineData(React, "frontend.charts=echarts", "web/app/src/components/chart.tsx", "useChartTheme")]
    [InlineData(Vue, "frontend.charts=echarts", "web/app/src/components/DataChart.vue", "useChartTheme")]
    [InlineData(React, "frontend.charts=chartjs", "web/app/src/components/chart.tsx", "useChartTheme")]
    [InlineData(Vue, "frontend.charts=chartjs", "web/app/src/components/DataChart.vue", "useChartTheme")]
    public void A_canvas_chart_reads_its_colours_from_the_theme(string profile, string charts, string chart, string reads)
    {
        var files = Files(profile, charts);

        files[chart].ShouldContain(reads);
        files.Keys.ShouldContain("web/app/src/components/chart-theme.ts");
        files.Keys.ShouldContain("web/app/src/components/chart-theme.test.ts");
        files.Keys.ShouldContain(chart.Replace(".tsx", ".test.tsx", StringComparison.Ordinal).Replace(".vue", ".test.ts", StringComparison.Ordinal));
    }

    [Fact]
    public void Recharts_colours_its_series_with_the_theme_variables()
    {
        var files = Files(React, "frontend.charts=recharts");

        files["web/app/src/components/chart-colors.ts"].ShouldContain("var(--chart-");
        files["web/app/src/components/chart.tsx"].ShouldContain("fill-muted-foreground");
        files.Keys.ShouldContain("web/app/src/components/chart-colors.test.ts");
    }

    /// <summary>Without shadcn or shadcn-vue there are no chart variables: the charts are as they were.</summary>
    [Theory]
    [InlineData(React, "frontend.charts=echarts", "frontend.ui-library=mui", "frontend.styling=css-modules")]
    [InlineData(React, "frontend.charts=chartjs", "frontend.ui-library=radix-headless")]
    [InlineData(React, "frontend.charts=recharts", "frontend.ui-library=radix-headless")]
    [InlineData(Vue, "frontend.charts=echarts", "frontend.ui-library=vuetify")]
    public void Without_the_theme_variables_the_charts_keep_their_library_colours(string profile, params string[] overrides)
    {
        var files = Files(profile, overrides);

        files.Keys.ShouldNotContain("web/app/src/components/chart-theme.ts");
        files.Keys.ShouldNotContain(file => file.StartsWith("web/app/src/components/chart", StringComparison.Ordinal) && file.Contains(".test.", StringComparison.Ordinal));
        files.Where(file => file.Key.StartsWith("web/app/src/components/chart", StringComparison.Ordinal))
            .ShouldAllBe(file => !file.Value.Contains("var(--chart-", StringComparison.Ordinal) && !file.Value.Contains("useChartTheme", StringComparison.Ordinal));
    }

    /// <summary>The web app builds, lints and formats, and the chart's tests pass: its theme colours, and dark mode and back.</summary>
    [Theory]
    [InlineData(React, "frontend.charts=echarts", "src/components/chart.test.tsx")]
    [InlineData(Vue, "frontend.charts=echarts", "src/components/DataChart.test.ts")]
    [InlineData(React, "frontend.charts=chartjs", "src/components/chart.test.tsx")]
    [InlineData(Vue, "frontend.charts=chartjs", "src/components/DataChart.test.ts")]
    public void A_canvas_chart_follows_the_theme_builds_and_its_tests_pass(string profile, string charts, string chartTest)
    {
        using var project = new GeneratedProject(profile, charts);
        var plain = DataTableDetailsTests.BuildAndTest(project);

        plain.ShouldContain("✓ src/components/chart-theme.test.ts > an oklch colour becomes the rgb a canvas chart library can parse, its alpha kept; any other colour stays as it is");
        plain.ShouldContain("✓ src/components/chart-theme.test.ts > in dark mode the chart theme is the dark palette's");
        plain.ShouldContain($"✓ {chartTest} > a chart follows the theme into dark mode and back");
    }

    [Fact]
    public void Recharts_follows_the_theme_builds_and_its_tests_pass()
    {
        using var project = new GeneratedProject(React, "frontend.charts=recharts");
        var plain = DataTableDetailsTests.BuildAndTest(project);

        plain.ShouldContain("✓ src/components/chart-colors.test.ts > a series takes the theme's --chart-1…5 in turn, which src/index.css swaps for dark mode, and starts again after the fifth");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

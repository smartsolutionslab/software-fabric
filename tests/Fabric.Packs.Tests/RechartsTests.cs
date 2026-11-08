using System.Text.RegularExpressions;
using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.charts.recharts (#275): Recharts in place of ECharts on React, through the project's <c>Chart</c>, which names
/// a chart for screen readers as ECharts' does, and the Recharts branch of the dashboards' widget view.
/// </summary>
public sealed partial class RechartsTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string WithDashboards = "saas-ai-react";
    private const string Recharts = "frontend.charts=recharts";

    /// <summary>A chart a developer would draw with the project's Chart and Recharts.</summary>
    private const string RevenueChart = """
        import { Bar, BarChart, XAxis, YAxis } from 'recharts';
        import { Chart } from '@/components/chart';
        import { chartColor } from '@/components/chart-colors';

        const months = [
          { month: 'January', revenue: 3 },
          { month: 'February', revenue: 4 },
        ];

        export function RevenueChart() {
          return (
            <Chart label="Revenue per month" height={240}>
              <BarChart data={months}>
                <XAxis dataKey="month" />
                <YAxis />
                <Bar dataKey="revenue" name="Revenue" fill={chartColor(0)} />
              </BarChart>
            </Chart>
          );
        }
        """;

    private const string RevenueChartTest = """
        import { cleanup, render, screen } from '@testing-library/react';
        import { afterEach, expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import { chartColor } from '@/components/chart-colors';
        import { RevenueChart } from './RevenueChart';

        // Vitest runs without globals, so Testing Library cannot register its own cleanup.
        afterEach(cleanup);

        test('a recharts chart is an image named by its label, with no axe violations', async () => {
          const { container } = render(<RevenueChart />);

          expect(screen.getByRole('img', { name: 'Revenue per month' })).toBeInTheDocument();
          expect(await axe(container)).toHaveNoViolations();
        });

        test('series colours come from one palette, which starts again after its last colour', () => {
          expect(chartColor(0)).toBe('#5470c6');
          expect(chartColor(9)).toBe(chartColor(0));
        });
        """;

    private const string OtherChartLibraries = """
        import * as echarts from 'echarts';
        import ReactECharts from 'echarts-for-react';
        import { Chart as ChartJs } from 'chart.js';

        export const others = [echarts, ReactECharts, ChartJs];
        """;

    [Fact]
    public void Recharts_replaces_echarts_in_the_dependencies_the_chart_component_the_rules_the_adr_and_the_lint_rule()
    {
        var files = Files(React, Recharts);

        files["web/app/package.json"].ShouldContain("\"recharts\": \"3.10.1\"");
        files["web/app/package.json"].ShouldContain("\"react-is\": \"19.3.1\"");
        files["web/app/package.json"].ShouldNotContain("echarts");
        files["web/app/eslint.config.js"].ShouldContain("group: ['echarts', 'echarts-for-react', 'chart.js', 'react-chartjs-2'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use Chart from src/components/chart (docs/adr).',");
        files["web/app/eslint.config.js"].ShouldNotContain("'recharts'");
        files["web/app/src/components/chart.tsx"].ShouldContain("import { ResponsiveContainer } from 'recharts';");
        files["web/app/src/components/chart.tsx"].ShouldContain("<div role=\"img\" aria-label={label}>");
        files["web/app/src/components/chart.tsx"].ShouldContain("<ResponsiveContainer width=\"100%\" height={height}>");
        files["web/app/src/components/chart-colors.ts"].ShouldContain("export function chartColor(position: number): string {");
        files["AGENTS.md"].ShouldContain("## Charts: Recharts");
        files["AGENTS.md"].ShouldContain("`chartColor(position)` from `@/components/chart-colors`");
        files["AGENTS.md"].ShouldNotContain("ECharts");
        files.Keys.ShouldContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-charts-recharts.md", StringComparison.Ordinal));
        files.Keys.ShouldNotContain(path => path.EndsWith("-frontend-charts-echarts.md", StringComparison.Ordinal));
        WebSources(files).ShouldNotContain(source => source.Value.Contains("echarts", StringComparison.OrdinalIgnoreCase), "no web source names echarts");
    }

    [Fact]
    public void With_vue_recharts_is_refused_in_one_sentence()
    {
        var resolution = ChoiceResolver.Resolve(Repository.Catalog, Vue, [Recharts]);

        resolution.Choices.ShouldNotBeNull();
        RuleEvaluator.Check(Repository.Catalog, resolution.Choices)
            .ShouldBe(["frontend.charts=recharts requires frontend.framework == react, but frontend.framework is vue."]);
    }

    [Fact]
    public void With_recharts_the_react_dashboards_draw_their_widgets_with_recharts_and_no_web_source_names_echarts()
    {
        var files = Files(WithDashboards, Recharts);

        var view = files["web/app/src/dashboards/WidgetView.tsx"];
        view.ShouldContain("from 'recharts';");
        view.ShouldContain("import { chartRows, type ChartVisual, pieSlices } from './chartData';");
        view.ShouldContain("<Chart label={title} height={height}>");
        view.ShouldNotContain("option");
        files.Keys.ShouldContain("web/app/src/dashboards/chartData.ts");
        files.Keys.ShouldContain("web/app/src/dashboards/chartData.test.ts");
        files.Keys.ShouldNotContain("web/app/src/dashboards/chartOption.ts");
        files.Keys.ShouldNotContain("web/app/src/dashboards/chartOption.test.ts");
        files["web/app/src/dashboards/DashboardEditor.test.tsx"].ShouldNotContain("vi.mock('@/components/chart'");
        files["web/app/src/dashboards/DashboardAssistant.test.tsx"].ShouldNotContain("vi.mock('@/components/chart'");
        WebSources(files).ShouldNotContain(source => source.Value.Contains("echarts", StringComparison.OrdinalIgnoreCase), "no web source names echarts");
    }

    [Fact]
    public void A_react_chart_drawn_with_recharts_builds_lints_is_found_by_its_name_and_lint_refuses_echarts_and_chart_js()
    {
        using var project = new GeneratedProject(React, Recharts);
        project.Write("web/app/src/pages/RevenueChart.tsx", RevenueChart);
        project.Write("web/app/src/pages/RevenueChart.test.tsx", RevenueChartTest);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/pages/RevenueChart.test.tsx > a recharts chart is an image named by its label, with no axe violations", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/RevenueChart.test.tsx > series colours come from one palette, which starts again after its last colour", customMessage: testOutput);
        project.Write("web/app/src/pages/OtherLibraries.ts", OtherChartLibraries);
        var (exitCode, lintOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, lintOutput);
        lintOutput.ShouldContain("'echarts' import is restricted from being used by a pattern. Use Chart from src/components/chart (docs/adr)");
        lintOutput.ShouldContain("'echarts-for-react' import is restricted from being used by a pattern. Use Chart from src/components/chart (docs/adr)");
        lintOutput.ShouldContain("'chart.js' import is restricted from being used by a pattern. Use Chart from src/components/chart (docs/adr)");
    }

    [Fact]
    public void With_recharts_the_dashboards_web_app_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(WithDashboards, Recharts);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/dashboards/chartData.test.ts > a bar or line chart has one row per category, each series by its position, a missing value left out", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/chartData.test.ts > a pie chart shows the first series, one slice per category", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.tsx", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.tsx", customMessage: testOutput);
        var (formatExitCode, format) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static IEnumerable<KeyValuePair<string, string>> WebSources(Dictionary<string, string> files) =>
        files.Where(file => file.Key.StartsWith("web/app/src/", StringComparison.Ordinal) && Regex.IsMatch(file.Key, @"\.(ts|tsx)$"));

    /// <summary>Installs, builds and lints the web workspace, runs the app's tests, and returns their verbose output.</summary>
    private static string InstallBuildLintAndTest(GeneratedProject project)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(Path.Combine(web, "app"), "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        return testOutput;
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

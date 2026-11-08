using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.charts.chartjs (#276): Chart.js in place of ECharts, on React through the project's <c>Chart</c> and on Vue
/// through its <c>DataChart</c>, each taking one Chart.js configuration and naming the chart for screen readers, and the
/// Chart.js branch of the dashboards' widget view on both.
/// </summary>
public sealed partial class ChartJsTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string WithDashboards = "saas-ai-react";
    private const string ChartJs = "frontend.charts=chartjs";

    /// <summary>What a Vue project needs for both dashboards, the editable and the generative.</summary>
    private static readonly string[] VueDashboards =
    [
        "backend.llm=anthropic",
        "backend.llm.agents=agent-framework",
        "frontend.agent-ui=ag-ui",
        "frontend.dashboards=user-editable,generative",
        ChartJs,
    ];

    /// <summary>A chart a developer would draw with the project's Chart and a Chart.js configuration.</summary>
    private const string RevenueChart = """
        import type { ChartConfiguration } from 'chart.js';
        import { Chart } from '@/components/chart';

        const revenue: ChartConfiguration = {
          type: 'bar',
          data: { labels: ['January', 'February'], datasets: [{ label: 'Revenue', data: [3, 4] }] },
        };

        export function RevenueChart() {
          return <Chart config={revenue} label="Revenue per month" height={240} />;
        }
        """;

    private const string RevenueChartTest = """
        import { cleanup, render, screen } from '@testing-library/react';
        import { afterEach, expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import { RevenueChart } from './RevenueChart';

        // Vitest runs without globals, so Testing Library cannot register its own cleanup.
        afterEach(cleanup);

        test('a chart drawn with Chart.js is an image named by its label, with no axe violations', async () => {
          const { container } = render(<RevenueChart />);

          expect(screen.getByRole('img', { name: 'Revenue per month' })).toBeInTheDocument();
          expect(await axe(container)).toHaveNoViolations();
        });
        """;

    private const string NotesChart = """
        <script setup lang="ts">
        import type { ChartConfiguration } from 'chart.js';
        import DataChart from '@/components/DataChart.vue';

        const notes: ChartConfiguration = {
          type: 'line',
          data: { labels: ['Monday', 'Tuesday'], datasets: [{ label: 'Notes', data: [3, 5] }] },
        };
        </script>

        <template>
          <DataChart :config="notes" label="Notes per day" :height="240" />
        </template>
        """;

    private const string NotesChartTest = """
        import { render, screen } from '@testing-library/vue';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import NotesChart from './NotesChart.vue';

        test('a chart drawn with Chart.js is an image named by its label, with no axe violations', async () => {
          const { container } = render(NotesChart);

          expect(screen.getByRole('img', { name: 'Notes per day' })).toBeInTheDocument();
          expect(await axe(container)).toHaveNoViolations();
        });
        """;

    private const string OtherReactChartLibraries = """
        import * as echarts from 'echarts';
        import ReactECharts from 'echarts-for-react';
        import { BarChart } from 'recharts';

        export const others = [echarts, ReactECharts, BarChart];
        """;

    private const string OtherVueChartLibraries = """
        import * as echarts from 'echarts';
        import VChart from 'vue-echarts';

        export const others = [echarts, VChart];
        """;

    [Fact]
    public void On_react_chart_js_replaces_echarts_in_the_dependencies_the_chart_component_the_rules_the_adr_and_the_lint_rule()
    {
        var files = Files(React, ChartJs);

        files["web/app/package.json"].ShouldContain("\"chart.js\": \"4.6.1\"");
        files["web/app/package.json"].ShouldContain("\"react-chartjs-2\": \"5.3.1\"");
        NamesECharts().IsMatch(files["web/app/package.json"]).ShouldBeFalse("package.json names no ECharts package");
        files["web/app/eslint.config.js"].ShouldContain("group: ['echarts', 'echarts-for-react', 'recharts'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use Chart from src/components/chart (docs/adr).',");
        files["web/app/eslint.config.js"].ShouldNotContain("'chart.js'");
        files["web/app/eslint.config.js"].ShouldNotContain("'react-chartjs-2'");
        var chart = files["web/app/src/components/chart.tsx"];
        chart.ShouldContain("import { type ChartConfiguration, Chart as ChartJs } from 'chart.js/auto';");
        chart.ShouldContain("import { Chart as ReactChart } from 'react-chartjs-2';");
        chart.ShouldContain("ChartJs.defaults.maintainAspectRatio = false;");
        chart.ShouldContain("<ReactChart type={config.type} data={config.data} options={config.options} role=\"img\" aria-label={label} />");
        files["AGENTS.md"].ShouldContain("## Charts: Chart.js");
        files["AGENTS.md"].ShouldContain("<Chart config={...} label=\"...\" />");
        NamesECharts().IsMatch(files["AGENTS.md"]).ShouldBeFalse("AGENTS.md names no ECharts");
        files.Keys.ShouldContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-charts-chartjs.md", StringComparison.Ordinal));
        files.Keys.ShouldNotContain(path => path.EndsWith("-frontend-charts-echarts.md", StringComparison.Ordinal));
        WebSources(files).ShouldNotContain(source => NamesECharts().IsMatch(source.Value), "no web source names echarts");
    }

    [Fact]
    public void On_vue_chart_js_replaces_echarts_in_the_dependencies_the_data_chart_component_the_rules_the_adr_and_the_lint_rule()
    {
        var files = Files(Vue, ChartJs);

        files["web/app/package.json"].ShouldContain("\"chart.js\": \"4.6.1\"");
        files["web/app/package.json"].ShouldContain("\"vue-chartjs\": \"5.3.4\"");
        NamesECharts().IsMatch(files["web/app/package.json"]).ShouldBeFalse("package.json names no ECharts package");
        files["web/app/eslint.config.js"].ShouldContain("group: ['echarts', 'vue-echarts'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use DataChart from src/components/DataChart.vue (docs/adr).',");
        files["web/app/eslint.config.js"].ShouldNotContain("'chart.js'");
        files["web/app/eslint.config.js"].ShouldNotContain("'vue-chartjs'");
        var chart = files["web/app/src/components/DataChart.vue"];
        chart.ShouldContain("import { type ChartConfiguration, Chart as ChartJs } from 'chart.js/auto';");
        chart.ShouldContain("import { Chart } from 'vue-chartjs';");
        chart.ShouldContain("ChartJs.defaults.maintainAspectRatio = false;");
        chart.ShouldContain("<Chart :type=\"config.type\" :data=\"config.data\" :options=\"config.options\" role=\"img\" :aria-label=\"label\" />");
        files.Keys.ShouldNotContain("web/app/src/components/chart.tsx");
        files["AGENTS.md"].ShouldContain("## Charts: Chart.js");
        files["AGENTS.md"].ShouldContain("<DataChart :config=\"...\" label=\"...\" />");
        NamesECharts().IsMatch(files["AGENTS.md"]).ShouldBeFalse("AGENTS.md names no ECharts");
        files.Keys.ShouldContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-charts-chartjs.md", StringComparison.Ordinal));
        WebSources(files).ShouldNotContain(source => NamesECharts().IsMatch(source.Value), "no web source names echarts");
    }

    [Fact]
    public void With_chart_js_the_react_dashboards_draw_their_widgets_from_a_chart_js_configuration_and_no_web_source_names_echarts()
    {
        var files = Files(WithDashboards, ChartJs);

        var view = files["web/app/src/dashboards/WidgetView.tsx"];
        view.ShouldContain("import { chartConfig, type ChartVisual } from './chartConfig';");
        view.ShouldContain("chartConfig(data.chart, visual as ChartVisual)");
        view.ShouldContain("<Chart config={config} label={title} height={height} />");
        view.ShouldNotContain("option");
        files.Keys.ShouldContain("web/app/src/dashboards/chartConfig.ts");
        files.Keys.ShouldContain("web/app/src/dashboards/chartConfig.test.ts");
        files.Keys.ShouldNotContain("web/app/src/dashboards/chartOption.ts");
        files.Keys.ShouldNotContain("web/app/src/dashboards/chartOption.test.ts");
        files.Keys.ShouldNotContain("web/app/src/dashboards/chartData.ts");
        files["web/app/src/dashboards/DashboardEditor.test.tsx"].ShouldContain("// Chart.js draws on a canvas jsdom does not have;");
        files["web/app/src/dashboards/DashboardAssistant.test.tsx"].ShouldContain("// Chart.js draws on a canvas jsdom does not have;");
        WebSources(files).ShouldNotContain(source => NamesECharts().IsMatch(source.Value), "no web source names echarts");
    }

    [Fact]
    public void With_chart_js_the_vue_dashboards_draw_their_widgets_from_the_same_configuration_as_reacts_and_no_web_source_names_echarts()
    {
        var files = Files(Vue, VueDashboards);
        var react = Files(WithDashboards, ChartJs);

        var view = files["web/app/src/dashboards/WidgetView.vue"];
        view.ShouldContain("import { chartConfig, type ChartVisual } from './chartConfig';");
        view.ShouldContain("chartConfig(data.value.chart, visual as ChartVisual)");
        view.ShouldContain("<DataChart v-else-if=\"config\" :config=\"config\" :label=\"title\" :height=\"height\" />");
        view.ShouldNotContain("option");
        files["web/app/src/dashboards/chartConfig.ts"].ShouldBe(react["web/app/src/dashboards/chartConfig.ts"]);
        files["web/app/src/dashboards/chartConfig.test.ts"].ShouldBe(react["web/app/src/dashboards/chartConfig.test.ts"]);
        files.Keys.ShouldNotContain("web/app/src/dashboards/chartOption.ts");
        files["web/app/src/dashboards/DashboardEditor.test.ts"].ShouldContain("props: { config: { type: Object, required: true },");
        files["web/app/src/dashboards/DashboardAssistant.test.ts"].ShouldContain("props: { config: { type: Object, required: true },");
        WebSources(files).ShouldNotContain(source => NamesECharts().IsMatch(source.Value), "no web source names echarts");
    }

    [Fact]
    public void A_react_chart_drawn_with_chart_js_builds_lints_is_found_by_its_name_and_lint_refuses_echarts_and_recharts()
    {
        using var project = new GeneratedProject(React, ChartJs);
        project.Write("web/app/src/pages/RevenueChart.tsx", RevenueChart);
        project.Write("web/app/src/pages/RevenueChart.test.tsx", RevenueChartTest);

        var testOutput = InstallBuildLintAndTest(project);

        Escapes().Replace(testOutput, string.Empty)
            .ShouldContain("✓ src/pages/RevenueChart.test.tsx > a chart drawn with Chart.js is an image named by its label, with no axe violations", customMessage: testOutput);
        project.Write("web/app/src/pages/OtherLibraries.ts", OtherReactChartLibraries);
        var (exitCode, lintOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, lintOutput);
        lintOutput.ShouldContain("'echarts' import is restricted from being used by a pattern. Use Chart from src/components/chart (docs/adr)");
        lintOutput.ShouldContain("'echarts-for-react' import is restricted from being used by a pattern. Use Chart from src/components/chart (docs/adr)");
        lintOutput.ShouldContain("'recharts' import is restricted from being used by a pattern. Use Chart from src/components/chart (docs/adr)");
    }

    [Fact]
    public void A_vue_chart_drawn_with_chart_js_builds_lints_is_found_by_its_name_and_lint_refuses_echarts()
    {
        using var project = new GeneratedProject(Vue, ChartJs);
        project.Write("web/app/src/pages/NotesChart.vue", NotesChart);
        project.Write("web/app/src/pages/NotesChart.test.ts", NotesChartTest);

        var testOutput = InstallBuildLintAndTest(project);

        Escapes().Replace(testOutput, string.Empty)
            .ShouldContain("✓ src/pages/NotesChart.test.ts > a chart drawn with Chart.js is an image named by its label, with no axe violations", customMessage: testOutput);
        project.Write("web/app/src/pages/OtherLibraries.ts", OtherVueChartLibraries);
        var (exitCode, lintOutput) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, lintOutput);
        lintOutput.ShouldContain("'echarts' import is restricted from being used by a pattern. Use DataChart from src/components/DataChart.vue (docs/adr)");
        lintOutput.ShouldContain("'vue-echarts' import is restricted from being used by a pattern. Use DataChart from src/components/DataChart.vue (docs/adr)");
    }

    [Fact]
    public void With_chart_js_the_react_dashboards_web_app_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(WithDashboards, ChartJs);

        var testOutput = InstallBuildLintTestAndFormat(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/dashboards/chartConfig.test.ts > a bar or line chart labels the categories and has one dataset per named series", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/chartConfig.test.ts > a pie chart shows the first series, one slice per category", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.tsx", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.tsx", customMessage: testOutput);
    }

    [Fact]
    public void With_chart_js_the_vue_dashboards_web_app_builds_lints_is_formatted_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Vue, VueDashboards);

        var testOutput = InstallBuildLintTestAndFormat(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/dashboards/chartConfig.test.ts > a bar or line chart labels the categories and has one dataset per named series", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/chartConfig.test.ts > a pie chart shows the first series, one slice per category", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts", customMessage: testOutput);
        plain.ShouldContain("✓ src/dashboards/DashboardAssistant.test.ts", customMessage: testOutput);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static IEnumerable<KeyValuePair<string, string>> WebSources(Dictionary<string, string> files) =>
        files.Where(file => file.Key.StartsWith("web/app/src/", StringComparison.Ordinal) && Regex.IsMatch(file.Key, @"\.(ts|tsx|vue)$"));

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

    private static string InstallBuildLintTestAndFormat(GeneratedProject project)
    {
        var testOutput = InstallBuildLintAndTest(project);
        var (formatExitCode, format) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);
        return testOutput;
    }

    /// <summary>ECharts by name (echarts, echarts-for-react, vue-echarts, EChartsOption), not the "echarts" inside "recharts".</summary>
    [GeneratedRegex("(?<![a-z])echarts", RegexOptions.IgnoreCase)]
    private static partial Regex NamesECharts();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}

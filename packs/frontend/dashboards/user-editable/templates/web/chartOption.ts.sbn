import type { EChartsOption } from 'echarts';
import type { ChartSeries } from '@/api/generated';

/** How a chart widget looks: bar, line or pie. */
export type ChartVisual = 'bar' | 'line' | 'pie';

/** The ECharts option for a widget's series: categories along the x axis, or, for a pie, one slice per category. */
export function chartOption(chart: ChartSeries, visual: ChartVisual): EChartsOption {
  if (visual === 'pie') {
    const [first] = chart.series;
    return {
      tooltip: { trigger: 'item' },
      series: [
        {
          type: 'pie',
          name: first?.name ?? '',
          data: chart.categories.map((category, index) => ({
            name: category,
            value: Number(first?.values[index] ?? 0),
          })),
        },
      ],
    };
  }

  return {
    tooltip: { trigger: 'axis' },
    legend: {},
    xAxis: { type: 'category', data: chart.categories },
    yAxis: { type: 'value' },
    series: chart.series.map((values) => ({ name: values.name, type: visual, data: values.values.map(Number) })),
  };
}

import type { EChartsOption } from 'echarts';
import ReactECharts from 'echarts-for-react';

interface ChartProps {
  option: EChartsOption;
  label: string;
  height?: number;
}

/** An ECharts chart; label names it for screen readers. */
export function Chart({ option, label, height = 320 }: ChartProps) {
  return (
    <div role="img" aria-label={label}>
      <ReactECharts option={option} style={ { height } } />
    </div>
  );
}

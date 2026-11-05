{{~ if project["frontend.framework"] == "vue" ~}}
## Charts: ECharts

- Draw charts with `<DataChart :option="..." label="..." />` from `@/components/DataChart.vue`; `label` says what the chart shows for screen readers.
- Type options as `EChartsOption`; keep them at module scope or in a `computed`.
{{~ else ~}}
## Charts: ECharts

- Draw charts with `<Chart option={...} label="..." />` from `@/components/chart`; `label` says what the chart shows for screen readers.
- Type options as `EChartsOption`; keep them at module scope or memoised.
{{~ end ~}}

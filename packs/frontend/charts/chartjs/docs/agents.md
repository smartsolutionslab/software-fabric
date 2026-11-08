{{~ if project["frontend.framework"] == "vue" ~}}
## Charts: Chart.js

- Draw charts with `<DataChart :config="..." label="..." />` from `@/components/DataChart.vue`; `config` is one Chart.js configuration (`type`, `data`, `options`), and `label` says what the chart shows for screen readers.
- Type the configuration as `ChartConfiguration` from `chart.js`; keep it at module scope or in a `computed`.
- Leave the colours to Chart.js's palette unless a series needs its own.
{{~ else ~}}
## Charts: Chart.js

- Draw charts with `<Chart config={...} label="..." />` from `@/components/chart`; `config` is one Chart.js configuration (`type`, `data`, `options`), and `label` says what the chart shows for screen readers.
- Type the configuration as `ChartConfiguration` from `chart.js`; keep it at module scope or memoised.
- Leave the colours to Chart.js's palette unless a series needs its own.
{{~ end ~}}

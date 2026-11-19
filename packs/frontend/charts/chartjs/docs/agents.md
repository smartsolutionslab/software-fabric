{{~ if project["frontend.framework"] == "vue" ~}}
## Charts: Chart.js

- Draw charts with `<DataChart :config="..." label="..." />` from `@/components/DataChart.vue`; `config` is one Chart.js configuration (`type`, `data`, `options`), and `label` says what the chart shows for screen readers.
- Type the configuration as `ChartConfiguration` from `chart.js`; keep it at module scope or in a `computed`.
{{~ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") ~}}
- Leave colours to the theme: a series without its own takes the next of `--chart-1…5`, the text and grid lines take the theme's, and the chart follows dark mode. Change a colour in `src/index.css`, never with a hex value in a dataset.
{{~ else ~}}
- Leave the colours to Chart.js's palette unless a series needs its own.
{{~ end ~}}
{{~ else ~}}
## Charts: Chart.js

- Draw charts with `<Chart config={...} label="..." />` from `@/components/chart`; `config` is one Chart.js configuration (`type`, `data`, `options`), and `label` says what the chart shows for screen readers.
- Type the configuration as `ChartConfiguration` from `chart.js`; keep it at module scope or memoised.
{{~ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") ~}}
- Leave colours to the theme: a series without its own takes the next of `--chart-1…5`, the text and grid lines take the theme's, and the chart follows dark mode. Change a colour in `src/index.css`, never with a hex value in a dataset.
{{~ else ~}}
- Leave the colours to Chart.js's palette unless a series needs its own.
{{~ end ~}}
{{~ end ~}}

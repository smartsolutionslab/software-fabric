{{~ if project["frontend.framework"] == "vue" ~}}
## Charts: ECharts

- Draw charts with `<DataChart :option="..." label="..." />` from `@/components/DataChart.vue`; `label` says what the chart shows for screen readers.
- Type options as `EChartsOption`; keep them at module scope or in a `computed`.
{{~ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") ~}}
- Leave colours to the theme: the chart draws its series in `--chart-1…5` and its text and lines in the theme's colours, and follows dark mode. Change a colour in `src/index.css`, never with a hex value in an option.
{{~ end ~}}
{{~ else ~}}
## Charts: ECharts

- Draw charts with `<Chart option={...} label="..." />` from `@/components/chart`; `label` says what the chart shows for screen readers.
- Type options as `EChartsOption`; keep them at module scope or memoised.
{{~ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") ~}}
- Leave colours to the theme: the chart draws its series in `--chart-1…5` and its text and lines in the theme's colours, and follows dark mode. Change a colour in `src/index.css`, never with a hex value in an option.
{{~ end ~}}
{{~ end ~}}

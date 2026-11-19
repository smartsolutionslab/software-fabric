{{~ if project["frontend.framework"] == "vue" ~}}
# Charts: Apache ECharts

**Status:** Accepted (generated)

## Decision

Charts are Apache ECharts through `vue-echarts`, wrapped by `src/components/DataChart.vue`, which gives every chart an accessible name and has it follow its container's size (`autoresize`), as echarts-for-react does. It imports the whole of ECharts; a project that wants a smaller bundle registers only the charts it draws there (`use([...])` from `echarts/core`).{{ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") }}

It draws in the theme's colours and follows dark mode: `src/components/chart-theme.ts` reads the series' palette from `--chart-1…5` and the text and lines from `--foreground`, `--muted-foreground` and `--border` in `src/index.css`, turning oklch into the rgb a canvas library's colour parser understands, and reads them again whenever the theme on `<html>` changes (#693), which draws the chart again. The theme is ECharts' (`echartsTheme`), so a colour an option sets itself wins over it.{{ end }}
{{~ else ~}}
# Charts: Apache ECharts

**Status:** Accepted (generated)

## Decision

Charts are Apache ECharts through `echarts-for-react`, wrapped by `src/components/chart.tsx`, which gives every chart an accessible name.{{ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") }}

It draws in the theme's colours and follows dark mode: `src/components/chart-theme.ts` reads the series' palette from `--chart-1…5` and the text and lines from `--foreground`, `--muted-foreground` and `--border` in `src/index.css`, turning oklch into the rgb a canvas library's colour parser understands, and reads them again whenever the theme on `<html>` changes (#693), which draws the chart again. The theme is ECharts' (`echartsTheme`), so a colour an option sets itself wins over it.{{ end }}
{{~ end ~}}

{{~ if project["frontend.framework"] == "vue" ~}}
# Charts: Chart.js

**Status:** Accepted (generated)

## Decision

Charts are Chart.js through `vue-chartjs`, wrapped by `src/components/DataChart.vue`, which takes one Chart.js configuration (`type`, `data`, `options`), names the chart for screen readers on its canvas (`role="img"`, `aria-label`), and has it fill a container as high as `height` and as wide as its parent. It imports `chart.js/auto`, which registers every chart type and plugin; a project that wants a smaller bundle registers only the ones it draws there (`Chart.register(...)` from `chart.js`).

{{ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") }}It draws in the theme's colours and follows dark mode: `src/components/chart-theme.ts` reads the series' palette from `--chart-1…5` and the text and lines from `--foreground`, `--muted-foreground` and `--border` in `src/index.css`, turning oklch into the rgb a canvas library's colour parser understands, and reads them again whenever the theme on `<html>` changes (#693), which draws the chart again. A dataset without colours of its own takes the palette's next one (a pie, doughnut or polar area chart one per slice), and the options' text and grid colours are the theme's unless the configuration sets them (`withChartTheme`, which leaves the configuration given as it was).{{ else }}Series take their colours from Chart.js's built-in palette (its Colors plugin) unless a dataset sets its own.{{ end }}

Chart.js draws on a canvas, which jsdom does not have, so a component test finds a chart by its name but sees nothing drawn; the end-to-end tests run in a browser, which draws it.
{{~ else ~}}
# Charts: Chart.js

**Status:** Accepted (generated)

## Decision

Charts are Chart.js through `react-chartjs-2`, wrapped by `src/components/chart.tsx`'s `Chart`, which takes one Chart.js configuration (`type`, `data`, `options`), names the chart for screen readers on its canvas (`role="img"`, `aria-label`), and has it fill a container as high as `height` and as wide as its parent. It imports `chart.js/auto`, which registers every chart type and plugin; a project that wants a smaller bundle registers only the ones it draws there (`Chart.register(...)` from `chart.js`).

{{ if object.has_key(project, "frontend.ui-library") && (project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue") }}It draws in the theme's colours and follows dark mode: `src/components/chart-theme.ts` reads the series' palette from `--chart-1…5` and the text and lines from `--foreground`, `--muted-foreground` and `--border` in `src/index.css`, turning oklch into the rgb a canvas library's colour parser understands, and reads them again whenever the theme on `<html>` changes (#693), which draws the chart again. A dataset without colours of its own takes the palette's next one (a pie, doughnut or polar area chart one per slice), and the options' text and grid colours are the theme's unless the configuration sets them (`withChartTheme`, which leaves the configuration given as it was).{{ else }}Series take their colours from Chart.js's built-in palette (its Colors plugin) unless a dataset sets its own.{{ end }}

Chart.js draws on a canvas, which jsdom does not have, so a component test finds a chart by its name but sees nothing drawn; the end-to-end tests run in a browser, which draws it.
{{~ end ~}}

{{~ if project["frontend.framework"] == "vue" ~}}
# Charts: Chart.js

**Status:** Accepted (generated)

## Decision

Charts are Chart.js through `vue-chartjs`, wrapped by `src/components/DataChart.vue`, which takes one Chart.js configuration (`type`, `data`, `options`), names the chart for screen readers on its canvas (`role="img"`, `aria-label`), and has it fill a container as high as `height` and as wide as its parent. It imports `chart.js/auto`, which registers every chart type and plugin; a project that wants a smaller bundle registers only the ones it draws there (`Chart.register(...)` from `chart.js`).

Series take their colours from Chart.js's built-in palette (its Colors plugin) unless a dataset sets its own.

Chart.js draws on a canvas, which jsdom does not have, so a component test finds a chart by its name but sees nothing drawn; the end-to-end tests run in a browser, which draws it.
{{~ else ~}}
# Charts: Chart.js

**Status:** Accepted (generated)

## Decision

Charts are Chart.js through `react-chartjs-2`, wrapped by `src/components/chart.tsx`'s `Chart`, which takes one Chart.js configuration (`type`, `data`, `options`), names the chart for screen readers on its canvas (`role="img"`, `aria-label`), and has it fill a container as high as `height` and as wide as its parent. It imports `chart.js/auto`, which registers every chart type and plugin; a project that wants a smaller bundle registers only the ones it draws there (`Chart.register(...)` from `chart.js`).

Series take their colours from Chart.js's built-in palette (its Colors plugin) unless a dataset sets its own.

Chart.js draws on a canvas, which jsdom does not have, so a component test finds a chart by its name but sees nothing drawn; the end-to-end tests run in a browser, which draws it.
{{~ end ~}}

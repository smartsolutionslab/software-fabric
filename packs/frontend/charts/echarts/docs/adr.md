{{~ if project["frontend.framework"] == "vue" ~}}
# Charts: Apache ECharts

**Status:** Accepted (generated)

## Decision

Charts are Apache ECharts through `vue-echarts`, wrapped by `src/components/DataChart.vue`, which gives every chart an accessible name and has it follow its container's size (`autoresize`), as echarts-for-react does. It imports the whole of ECharts; a project that wants a smaller bundle registers only the charts it draws there (`use([...])` from `echarts/core`).
{{~ else ~}}
# Charts: Apache ECharts

**Status:** Accepted (generated)

## Decision

Charts are Apache ECharts through `echarts-for-react`, wrapped by `src/components/chart.tsx`, which gives every chart an accessible name.
{{~ end ~}}

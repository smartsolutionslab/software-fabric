# Charts: Recharts

**Status:** Accepted (generated)

## Decision

Charts are Recharts, drawn inside `src/components/chart.tsx`'s `Chart`, which gives every chart an accessible name and makes it as wide as its container (`ResponsiveContainer`). A Recharts chart is composed in JSX, `BarChart`, `LineChart` or `PieChart` with its axes and series, not described by an option object, so `Chart` takes the chart as its child.

Recharts brings no palette, so the series take their colours in turn from `chartColor(position)` in `src/components/chart-colors.ts`. Its colours are ECharts' default palette, so a project looks the same with either chart library.

jsdom has no layout, so a component test finds a chart by its name but sees none of its shapes; the end-to-end tests run in a browser, which draws them.

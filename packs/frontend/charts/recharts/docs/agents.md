## Charts: Recharts

- Draw a chart as one Recharts chart inside `<Chart label="..." height={...}>` from `@/components/chart`: `<Chart label="Revenue per month"><BarChart data={rows}>...</BarChart></Chart>`. `label` says what the chart shows for screen readers.
- Colour each series, or each pie slice, with `chartColor(position)` from `@/components/chart-colors`, so every chart shares one palette.
- Keep a chart's `data` at module scope or memoised.

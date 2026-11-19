## Charts: Recharts

- Draw a chart as one Recharts chart inside `<Chart label="..." height={...}>` from `@/components/chart`: `<Chart label="Revenue per month"><BarChart data={rows}>...</BarChart></Chart>`. `label` says what the chart shows for screen readers.
{{~ if object.has_key(project, "frontend.ui-library") && project["frontend.ui-library"] == "shadcn" ~}}
- Colour each series, or each pie slice, with `chartColor(position)` from `@/components/chart-colors`: the theme's `--chart-1…5`, which follow dark mode. Never a hex value.
{{~ else ~}}
- Colour each series, or each pie slice, with `chartColor(position)` from `@/components/chart-colors`, so every chart shares one palette.
{{~ end ~}}
- Keep a chart's `data` at module scope or memoised.

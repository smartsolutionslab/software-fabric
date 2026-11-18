## Colour scheme: green

- The palettes in `src/index.css` are shadcn/ui's slate in its green scheme: the primary colour, the sidebar's active item, the focus rings and the charts are green, everything else slate.
- To switch to another scheme (blue, green or orange), run `fabric change --set frontend.color-scheme=<scheme>` in a clean working tree: it repaints the palettes (and a design system's brand tokens), records the scheme in `fabric.yaml` and keeps the project's other edits, as a merge left uncommitted for review. Never edit the scheme in `fabric.yaml` or a palette colour by hand; `fabric upgrade` keeps the recorded scheme.

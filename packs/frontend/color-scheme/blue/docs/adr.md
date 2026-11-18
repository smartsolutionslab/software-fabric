# Colour scheme: blue

**Status:** Accepted (generated)

## Decision

The UI library's theme is shadcn/ui's slate theme in shadcn/ui's blue colour scheme (`apps/v4/registry/themes.ts` in shadcn-ui/ui): the light and the dark palette in `src/index.css` keep slate's neutrals (backgrounds, text, borders, muted and secondary surfaces) and take the scheme's blue for the primary colour (the default button), the sidebar's active item, the focus rings and the five chart colours. The values are the registry's Tailwind blue shades, with two departures, so that every text colour has WCAG AA's 4.5:1 on its background in both themes: the sidebar's active item takes the primary colour, because the registry's lighter tone for it (Tailwind's blue-600 light, blue-500 dark) has less than 4.5:1 under the light text on it, and that tone colours the focus rings instead, which the registry leaves to the base colour. The scheme does not change the registry's secondary colour, which stays slate's.
{{~ if project["frontend.design-system"] != "none" ~}}

The design system's brand tokens (`color.brand.500`, `700` and `800` in `web/packages/tokens/src/color.tokens.json`) are the same blue shades, so its primary colour is the scheme's in both themes; its other colours keep their values, with a dark value of their own.
{{~ end ~}}

## Consequences

- The scheme is a choice recorded in `fabric.yaml` (`frontend.color-scheme`); its colours are not edited by hand.
- Another scheme is a change of that choice: `fabric change --set frontend.color-scheme=<scheme>` merges the other scheme's palettes into the project and records it, keeping the project's other edits. `fabric upgrade` keeps the recorded scheme and brings its changes in a newer generator.

# Colour scheme: orange

**Status:** Accepted (generated)

## Decision

The UI library's theme is shadcn/ui's slate theme in shadcn/ui's orange colour scheme (`apps/v4/registry/themes.ts` in shadcn-ui/ui): the light and the dark palette in `src/index.css` keep slate's neutrals (backgrounds, text, borders, muted and secondary surfaces) and take the scheme's orange for the primary colour (the default button), the sidebar's active item, the focus rings and the five chart colours. The values are the registry's Tailwind orange shades, with three departures, so that every text colour has WCAG AA's 4.5:1 on its background in both themes: the sidebar's active item takes the primary colour, because the registry's lighter tone for it (Tailwind's orange-600 light, orange-500 dark) has less than 4.5:1 under the light text on it, and that tone colours the focus rings instead, which the registry leaves to the base colour. And the dark primary, with the sidebar's active item, is the orange-400 tone (`oklch(0.75 0.183 55.934)`) under the orange-950 tone, not the registry's orange-800 under the light text, because the primary is also text on the page (the button's link variant): orange-800 has 2.7:1 on the dark background, orange-400 has 8.5:1 there and 6.6:1 under orange-950, and no tone has 4.5:1 both on the dark background and under a light text. The light primary has 5.2:1 as text on the light background. The scheme does not change the registry's secondary colour, which stays slate's.
{{~ if project["frontend.design-system"] != "none" ~}}

The design system's brand tokens (`color.brand.400`, `500`, `700` and `800` in `web/packages/tokens/src/color.tokens.json`) are the same orange shades, so its primary colour is the scheme's in both themes (`700` light, `400` dark); its accent (`color.signal.500`) is the scheme's own, apart from the primary, as the design system's decision record says; its other colours keep their values, with a dark value of their own.
{{~ end ~}}

## Consequences

- The scheme is a choice recorded in `fabric.yaml` (`frontend.color-scheme`), made when the project is generated; its colours are not edited by hand.
- `fabric upgrade` keeps `fabric.yaml`'s choices, so it brings this scheme's changes in a newer generator, not another scheme.

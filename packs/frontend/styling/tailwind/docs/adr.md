# Styling: Tailwind CSS

**Status:** Accepted (generated)

## Decision

Tailwind CSS 4 through its Vite plugin; styles are utility classes in the markup. `src/index.css` imports Tailwind and holds the few global rules and theme variables.

## Reduced motion

Every animation and transition runs only for a reader whose system does not ask for reduced motion (WCAG 2.3.3): its class carries `motion-safe:`, Tailwind's variant for `@media (prefers-reduced-motion: no-preference)`, so with `reduce` the element changes at once. The components the UI library brings are generated that way, and lint keeps it: the rule `motion/respect-reduced-motion` in `eslint.config.js` fails an `animate-*` or `transition*` class (but `animate-none` and `transition-none`) without `motion-safe:` or `motion-reduce:`, in any string of a script under `src/`{{ if project["frontend.framework"] == "vue" }} and in a template's attributes{{ end }}. Tests are left out.

## Consequences

- A component a CLI adds (shadcn's `add`) needs `motion-safe:` before each animation class before lint passes.
- The rule reads strings, not markup it cannot see: a class built from pieces (`'animate-' + name`) or written in a stylesheet escapes it, and the word "transition" alone in a sentence trips it; say it another way.
- Animations a library draws in its own stylesheet are the library's: they are not classes here.

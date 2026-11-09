# Styling: CSS Modules

**Status:** Accepted (generated)

## Decision

A component's styles live in a `*.module.css` beside it, imported as `styles` and applied by class name
(`styles.button`); Vite compiles CSS Modules itself, so the names are scoped to the component without a plugin. No
utility classes in the markup. `src/index.css` holds the few global rules that a utility framework's reset would
otherwise give: box sizing, and the page's margin and font.

## Consequences

- A component's look sits in one file next to it, and two components cannot clash over a class name.
- Vitest replaces a module's import with a proxy that answers every class name, so a component test passes even when
  the stylesheet has no rule for the class it checks. `src/cssModules.test.ts` reads the stylesheets instead: every
  `styles.<name>` a component under `src/` uses needs a rule with at least one declaration in the module it imports.
- The check sees class names written out (`styles.button`); one looked up by a computed name (`styles[name]`) is not
  checked, so write them out.
- A class only under `:global` is not the module's, so the check does not count it. The check reads flat rules: a
  class styled only through native nesting (`.a { &:hover { … } }`) is reported unstyled, so keep module rules flat.

{{~ css_modules = project["frontend.styling"] == "css-modules" ~}}
# UI components: Radix primitives with {{ if css_modules }}CSS Modules{{ else }}Tailwind{{ end }}

**Status:** Accepted (generated)

## Decision

{{~ if css_modules ~}}
Components are built on Radix's headless primitives (MIT), styled with CSS Modules, and live in `src/components/ui/`, owned by the project. `button.tsx` is the first; others follow its shape (a primitive from `@radix-ui/react-*`, its styles in a `.module.css` beside it, variants with `class-variance-authority` over the module's classes, classes joined with `cn` from `src/lib/utils.ts`). The button has the variants default, outline and ghost and the sizes default, sm and lg. No other component library.
{{~ else ~}}
Components are built on Radix's headless primitives (MIT), styled with Tailwind, and live in `src/components/ui/`, owned by the project: the primitives shadcn/ui is made of, without its registry and theme. `button.tsx` is the first; others follow its shape (a primitive from `@radix-ui/react-*`, variants with `class-variance-authority`, classes merged with `cn` from `src/lib/utils.ts`). There is no theme of CSS variables, so classes name colours of Tailwind's palette (slate for surfaces and text, red for errors). The button has the variants default, outline and ghost and the sizes default, sm and lg. No other component library.
{{~ end ~}}

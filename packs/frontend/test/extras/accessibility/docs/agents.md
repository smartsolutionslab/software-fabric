## Accessibility tests

{{~ if project["frontend.architecture"] == "spa" ~}}
- Every screen has an axe check, `expect(await axe(container)).toHaveNoViolations()`, in `<Name>Page.axe.test.*` beside it. `fabric add-module` generates it with a module's page; a screen you write gets one too. It runs whatever the screen's own `<Name>Page.test.*` does, so leave it in place; `src/screens.test.ts` fails without it.
{{~ else ~}}
- Every new screen gets an axe check in its component test: `expect(await axe(container)).toHaveNoViolations()`.
{{~ end ~}}
- Fix the markup (labels, roles, headings), never silence a rule.

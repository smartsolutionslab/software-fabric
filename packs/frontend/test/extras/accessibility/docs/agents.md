## Accessibility tests

- Every new screen gets an axe check in its component test: `expect(await axe(container)).toHaveNoViolations()`.
{{~ if project["frontend.architecture"] == "spa" ~}}
- A module's generated `<Name>Page.axe.test.*` checks its screen until the screen's own `<Name>Page.test.*` has an axe check; it then skips itself. Leave it as generated.
{{~ end ~}}
- Fix the markup (labels, roles, headings), never silence a rule.

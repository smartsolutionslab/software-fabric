## Accessibility tests

- Every new screen gets an axe check in its component test: `expect(await axe(container)).toHaveNoViolations()`.
- Fix the markup (labels, roles, headings), never silence a rule.

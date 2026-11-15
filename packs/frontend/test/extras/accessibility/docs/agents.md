{{~ client = object.has_key(project, "frontend.api-client") ? project["frontend.api-client"] : "" ~}}
{{~ stands_in = project["frontend.architecture"] == "spa" && (client == "rtk-query-codegen" || (client == "orval" && (project["frontend.framework"] == "react" || !object.has_key(project, "frontend.state") || project["frontend.state"] != "pinia"))) ~}}
## Accessibility tests

{{~ if project["frontend.architecture"] == "spa" ~}}
- Every screen has an axe check, `expect(await axe(container)).toHaveNoViolations()`, in `<Name>Page.axe.test.*` beside it. `fabric add-module` generates it with a module's page; a screen you write gets one too. It runs whatever the screen's own `<Name>Page.test.*` does, so leave it in place; `src/screens.test.ts` fails without it.
- The axe check asks nothing of what a screen shows, only that axe finds no violations in it, so a screen you rewrite needs no generated heading or section to pass it. Test what the screen does in its own tests{{ if object.has_key(project, "backend.authorization") && array.size(project["backend.authorization"]) > 0 }}; the generated page's Manage section is checked in `<Name>Page.probe.test.*`, which is yours once you rewrite the page{{ end }}.
{{~ if object.has_key(project, "frontend.auth-client") ~}}
- With browser sign-in, the axe check renders the screen for the local user `developer`, signed in: {{ if object.has_key(project, "backend.authorization") && array.size(project["backend.authorization"]) > 0 }}what `RequirePermission`, `useHasRole` and `useHasClaim` show the developer is checked, what they show only to other users is not{{ else }}a screen that reads the signed-in user renders as the developer sees it{{ end }}.
{{~ end ~}}
{{~ if stands_in ~}}
- Fill in the screen's `<Name>Page.data.ts`, beside its axe check: an example answer per hook of the generated client the screen calls (`export const examples: Record<string, unknown> = { {{ if client == "rtk-query-codegen" }}useGetInvoicesQuery{{ else }}useGetInvoices{{ end }}: { items: [...], totalCount: 1, page: 1, pageSize: 20 } };`). The axe check then also renders the screen with that data; while the file is empty, the screen is checked only as it loads, and whatever it shows once data comes goes unchecked.
{{~ end ~}}
{{~ else ~}}
- Every new screen gets an axe check in its component test: `expect(await axe(container)).toHaveNoViolations()`.
{{~ end ~}}
- Fix the markup (labels, roles, headings), never silence a rule.

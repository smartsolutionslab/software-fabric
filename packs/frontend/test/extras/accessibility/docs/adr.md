# Test extra: accessibility

**Status:** Accepted (generated)

## Decision

Component tests check rendered screens with axe (`vitest-axe`): `expect(await axe(container)).toHaveNoViolations()`. jsdom cannot judge contrast or layout, so this catches structure (names, roles, labels), not everything; end-to-end axe checks with Playwright follow when screens exist to check.
{{~ client = object.has_key(project, "frontend.api-client") ? project["frontend.api-client"] : "" ~}}
{{~ stands_in = project["frontend.architecture"] == "spa" && (client == "rtk-query-codegen" || (client == "orval" && (project["frontend.framework"] == "react" || !object.has_key(project, "frontend.state") || project["frontend.state"] != "pinia"))) ~}}
{{~ if project["frontend.architecture"] == "spa" ~}}

Each module screen's axe check is a file of its own beside it, `<Name>Page.axe.test.*`, which `add-module` generates and which a screen written by hand gets too; `src/screens.test.ts` fails while a screen has none. It always runs, standing in for the generated API client so that it renders the real screen, so an assertion skipped or commented out in the screen's own `<Name>Page.test.*` never leaves the screen unchecked. A screen whose own test also runs axe is checked twice, which is accepted. The screen's own component test stays the project's, so an upgrade never writes over it.
{{~ if stands_in ~}}

The stand-in answers every hook as still loading, so on its own the check sees only the screen as it first renders. Beside it the generator writes `<Name>Page.data.ts`, an empty file the project owns: an example answer per hook there makes the check render the screen a second time, populated with that data. A screen whose data file stays empty is checked in its loading state only, and whatever it shows once data comes is not checked; that is the limit of a check the generator writes without knowing the screen's data. A screen that needs a provider other than the API client (a Pinia store, TanStack Query around openapi-fetch) gets that provider in its axe file by hand.
{{~ end ~}}
{{~ end ~}}

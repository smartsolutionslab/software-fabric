namespace Fabric.Packs.Tests;

/// <summary>lean-crud's app, run by its AppHost: a user signs in through Keycloak and lists a module's data through the generated orval hooks.</summary>
[Collection(FixedPorts.Name)]
public sealed class LeanCrudSignInTests
{
    private const string NotesEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api;

        public sealed record Note(Guid Id, string Text);

        public sealed class Notes : IEndpoint
        {
            private static readonly Note[] All = [new(Guid.Parse("0199c0de-0000-7000-8000-000000000001"), "first note"), new(Guid.Parse("0199c0de-0000-7000-8000-000000000002"), "second note")];

            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/notes", () => TypedResults.Ok(All));
        }
        """;

    private const string NotesPage = """
        import { type Note, useGetBillingNotes } from '@/api/generated';
        import { DataTable } from '@/components/ui/data-table';
        import { columnsOf } from '@/components/ui/data-table-columns';

        const notes = columnsOf<Note>();
        const columns = notes.columns([notes.accessor('text', { header: 'Text' })]);
        const none: Note[] = [];

        export function BillingPage() {
          const { data = none, isLoading } = useGetBillingNotes();
          return (
            <main>
              <h1>Billing</h1>
              {isLoading ? <p>Loading…</p> : <DataTable columns={columns} data={data} />}
            </main>
          );
        }
        """;

    private const string NotesSpec = """
        import { expect, test } from '@playwright/test';

        test('a signed-in user sees the billing notes', async ({ page }) => {
          await page.goto('/');
          await page.getByLabel('Username or email').fill('developer');
          await page.getByLabel('Password', { exact: true }).fill('developer');
          await page.getByRole('button', { name: 'Sign In' }).click();

          await page.getByRole('link', { name: 'Billing' }).click();

          await expect(page.getByRole('cell', { name: 'first note' })).toBeVisible();
          await expect(page.getByRole('cell', { name: 'second note' })).toBeVisible();
        });
        """;

    [Fact]
    public void A_user_signs_in_through_keycloak_and_sees_a_modules_data_through_the_generated_hooks()
    {
        using var project = new GeneratedProject("lean-crud");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/Notes.cs", NotesEndpoint);
        project.Build();

        // The new endpoint makes the frontend's OpenAPI copy stale: the integration tests say so and rewrite it.
        var (_, integration) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");
        integration.ShouldContain(
            "failed Shop.Integration.Tests.OpenApiDocumentTests.The_frontends_copy_of_the_openapi_document_is_current",
            customMessage: integration);
        var web = Path.Combine(project.ProjectDirectory, "web");
        File.ReadAllText(Path.Combine(web, "app", "openapi.json")).ShouldContain("/billing/notes");

        project.Write("web/app/src/modules/billing/BillingPage.tsx", NotesPage);
        project.Write("web/app/e2e/billing.spec.ts", NotesSpec);
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        // The generated CI's own end-to-end step: start the AppHost, wait for the app, run Playwright, stop.
        var (e2eExitCode, e2eOutput) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/e2e.sh");
        e2eExitCode.ShouldBe(0, e2eOutput);
        e2eOutput.ShouldContain("2 passed");
    }
}

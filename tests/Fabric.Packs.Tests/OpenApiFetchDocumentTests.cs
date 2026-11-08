namespace Fabric.Packs.Tests;

/// <summary>
/// openapi-fetch's gate (#264), as orval's: a new endpoint makes web/app/openapi.json stale, the generated integration
/// tests say so and rewrite it, and the rewritten document lets the web app call the endpoint typed.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class OpenApiFetchDocumentTests
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
            private static readonly Note[] All = [new(Guid.Parse("0199c0de-0000-7000-8000-000000000001"), "first note")];

            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/notes", () => TypedResults.Ok(All));
        }
        """;

    private const string NotesPage = """
        import { useQuery } from '@tanstack/react-query';
        import { api } from '@/api/baseApi';

        export function BillingPage() {
          const { data = [] } = useQuery({
            queryKey: ['/billing/notes'],
            queryFn: async () => (await api.GET('/billing/notes')).data ?? [],
          });
          return (
            <main>
              <h1>Billing</h1>
              <ul>
                {data.map((note) => (
                  <li key={note.id}>{note.text}</li>
                ))}
              </ul>
            </main>
          );
        }
        """;

    [Fact]
    public void A_new_endpoint_fails_the_integration_tests_until_the_rewritten_document_is_used()
    {
        using var project = new GeneratedProject("lean-crud", "frontend.api-client=openapi-fetch");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api", NotesEndpoint);
        project.Build();

        var (_, integration) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");
        integration.ShouldContain(
            "failed Shop.Integration.Tests.OpenApiDocumentTests.The_frontends_copy_of_the_openapi_document_is_current",
            customMessage: integration);
        var web = Path.Combine(project.ProjectDirectory, "web");
        File.ReadAllText(Path.Combine(web, "app", "openapi.json")).ShouldContain("/billing/notes");

        project.Write("web/app/src/modules/billing/BillingPage.tsx", NotesPage);
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (_, again) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");
        again.ShouldNotContain("failed Shop.Integration.Tests.OpenApiDocumentTests", customMessage: again);
    }
}

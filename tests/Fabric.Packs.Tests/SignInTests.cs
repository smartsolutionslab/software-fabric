using System.Diagnostics;

namespace Fabric.Packs.Tests;

/// <summary>The M1-07 gate: the generated app, run by its AppHost, signs a user in through Keycloak and lists a module's data.</summary>
[Collection(FixedPorts.Name)]
public sealed class SignInTests
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
                endpoints.MapGet("/notes", () => Result<Note[], ApiError>.Success(All).ToHttpResult());
        }
        """;

    private const string NotesPage = """
        import { type Note, useGetBillingNotesQuery } from '@/api/generated';
        import { DataTable } from '@/components/ui/data-table';
        import { columnsOf } from '@/components/ui/data-table-columns';

        const notes = columnsOf<Note>();
        const columns = notes.columns([notes.accessor('text', { header: 'Text' })]);
        const none: Note[] = [];

        export function BillingPage() {
          const { data = none, isLoading } = useGetBillingNotesQuery();
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
    public async Task A_user_signs_in_through_keycloak_and_sees_a_modules_data()
    {
        using var project = new GeneratedProject("saas-standard-react");
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

        using var appHost = AppHost.Start(project.ProjectDirectory);
        await appHost.WaitUntilAnswering(new Uri("http://localhost:5173/"), TimeSpan.FromMinutes(5));

        var (e2eExitCode, e2eOutput) = Shell.Run(Path.Combine(web, "app"), "pnpm", "e2e");
        e2eExitCode.ShouldBe(0, $"pnpm e2e:\n{e2eOutput}\nAppHost:\n{appHost.Output}");
        e2eOutput.ShouldContain("2 passed");
    }

    /// <summary>The generated AppHost started with dotnet run, as a developer starts it.</summary>
    private sealed class AppHost : IDisposable
    {
        private readonly Process process;
        private readonly System.Text.StringBuilder output = new();

        private AppHost(Process process)
        {
            this.process = process;
            process.OutputDataReceived += (_, line) => Append(line.Data);
            process.ErrorDataReceived += (_, line) => Append(line.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public string Output
        {
            get
            {
                lock (output)
                {
                    return output.ToString();
                }
            }
        }

        public static AppHost Start(string projectDirectory)
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = projectDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "run", "--project", "src/AppHost", "--launch-profile", "http" })
            {
                start.ArgumentList.Add(argument);
            }

            return new AppHost(Process.Start(start)!);
        }

        public async Task WaitUntilAnswering(Uri address, TimeSpan timeout)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                try
                {
                    if ((await client.GetAsync(address)).IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    // Not up yet.
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            throw new InvalidOperationException($"{address} did not answer:\n{Output}");
        }

        public void Dispose()
        {
            // Ctrl+C lets Aspire stop what it started (containers, the Vite server); killing it would orphan them.
            using (var interrupt = Process.Start("kill", ["-INT", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
            {
                interrupt.WaitForExit();
            }

            if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(30));
            }

            process.Dispose();
        }

        private void Append(string? line)
        {
            lock (output)
            {
                output.AppendLine(line);
            }
        }
    }
}

using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.state: zustand-tanstack-query</c> in a React app (#262): server data in <c>@tanstack/react-query</c>'s cache,
/// provided around the app and read through orval's generated hooks, and client state in Zustand stores.
/// </summary>
public sealed class ZustandTanStackQueryTests
{
    private const string Profile = "lean-crud";
    private const string Zustand = "frontend.state=zustand-tanstack-query";
    private const string Provider = "web/app/src/providers/query.tsx";

    [Fact]
    public void A_react_project_with_zustand_and_tanstack_query_generates_from_the_real_packs_alone()
    {
        foreach (var (profile, overrides) in new[] { (Profile, new[] { Zustand }), ("saas-standard-react", [Zustand, "frontend.api-client=orval"]) })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides).Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        }
    }

    [Fact]
    public void The_app_provides_one_query_client_and_depends_on_zustand_without_a_redux_store()
    {
        var files = Files(Zustand);

        files[Provider].ShouldContain("import { QueryClient, QueryClientProvider } from '@tanstack/react-query';");
        files[Provider].ShouldContain("<QueryClientProvider client={queryClient}>{children}</QueryClientProvider>");
        files["web/app/src/main.tsx"].ShouldContain("import { withQueryClient } from './providers/query';");
        files["web/app/package.json"].ShouldContain("\"@tanstack/react-query\": \"5.104.5\"");
        files["web/app/package.json"].ShouldContain("\"zustand\": \"5.0.16\"");
        files["web/app/package.json"].ShouldNotContain("redux");
        files["web/app/orval.config.ts"].ShouldContain("client: 'react-query',");
        files.Keys.ShouldNotContain("web/app/src/store.ts");
        files.Keys.ShouldNotContain("web/app/src/providers/store.tsx");
    }

    [Fact]
    public void Its_rules_and_adr_keep_server_data_out_of_the_stores_and_lint_refuses_the_other_libraries_but_not_zustand()
    {
        var files = Files(Zustand);

        var lint = files["web/app/eslint.config.js"];
        lint.ShouldContain("'react-redux'");
        lint.ShouldContain("'jotai'");
        lint.ShouldContain("'swr'");
        lint.ShouldNotContain("'zustand'");
        files["AGENTS.md"].ShouldContain("## Frontend state: Zustand and TanStack Query");
        files["AGENTS.md"].ShouldContain("src/stores/");
        files["AGENTS.md"].ShouldNotContain("## Frontend state: TanStack Query\n");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-state-zustand-tanstack-query.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("@tanstack/react-query");
        adr.ShouldContain("never holds a copy of server data");
    }

    [Fact]
    public void The_web_app_builds_lints_and_passes_its_tests_with_a_zustand_store_and_lint_refuses_jotai()
    {
        using var project = new GeneratedProject(Profile, Zustand);
        project.AddModule("billing");
        project.Write("web/app/src/stores/selection.ts", """
            import { create } from 'zustand';

            interface Selection {
              noteId: string | null;
              select: (noteId: string | null) => void;
            }

            export const useSelection = create<Selection>()((set) => ({
              noteId: null,
              select: (noteId) => set({ noteId }),
            }));
            """);
        project.Write("web/app/src/stores/selection.test.ts", """
            import { expect, test } from 'vitest';
            import { useSelection } from '@/stores/selection';

            test('a selection is client state held in a zustand store', () => {
              useSelection.getState().select('note-1');

              expect(useSelection.getState().noteId).toBe('note-1');
            });
            """);

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        project.Write("web/app/src/cart.ts", """
            import { atom } from 'jotai';

            export const cart = atom(0);
            """);

        var (lintExitCode, lint) = Shell.Run(web, "pnpm", "lint");

        lintExitCode.ShouldNotBe(0, lint);
        lint.ShouldContain("'jotai' import is restricted from being used by a pattern. Server data: the generated TanStack Query hooks; client state: Zustand stores");
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

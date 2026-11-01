using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>lean-crud's frontend from the real packs: TanStack Query for server state, an orval client generated from the host's OpenAPI document.</summary>
public sealed class LeanCrudFrontendTests
{
    private const string Profile = "lean-crud";

    /// <summary>
    /// A test a developer would write: a generated call reaches the host through /api and carries the token oidc-client-ts stored for the
    /// signed-in user. It runs in the generated app's own Vitest.
    /// </summary>
    private const string TokenTest = """
        import { afterEach, expect, test, vi } from 'vitest';
        import { getMe } from '@/api/generated';
        import { oidcConfig } from '@/auth/oidcConfig';

        afterEach(() => {
          vi.unstubAllGlobals();
          sessionStorage.clear();
        });

        test('a generated call reaches the host through /api with the signed-in users token', async () => {
          sessionStorage.setItem(
            `oidc.user:${oidcConfig.authority}:${oidcConfig.client_id}`,
            JSON.stringify({ access_token: 'token-of-developer', token_type: 'Bearer', profile: { sub: 'developer' } }),
          );
          const fetch = vi.fn<typeof globalThis.fetch>(() =>
            Promise.resolve(new Response(JSON.stringify({ name: 'developer' }), { status: 200 })),
          );
          vi.stubGlobal('fetch', fetch);

          const me = await getMe();

          expect(me).toEqual({ name: 'developer' });
          const [url, init] = fetch.mock.calls[0] ?? [];
          expect(url).toBe('/api/me');
          expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer token-of-developer');
        });
        """;

    [Fact]
    public void A_lean_crud_project_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, []);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_lean_crud_web_app_installs_builds_lints_and_passes_its_tests()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");

        RunWeb(project, "install", "build", "lint", "test");
    }

    [Fact]
    public void The_lean_crud_web_app_passes_its_format_check_after_building()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        RunWeb(project, "install", "build");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");

        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
    }

    [Fact]
    public void A_generated_call_carries_the_signed_in_users_token()
    {
        using var project = new GeneratedProject(Profile);
        project.Write("web/app/src/api/token.test.ts", TokenTest);

        RunWeb(project, "install", "test");
    }

    [Fact]
    public void Importing_another_state_library_fails_lint()
    {
        using var project = new GeneratedProject(Profile);
        project.Write("web/app/src/cart.ts", """
            import { create } from 'zustand';

            export const useCart = create(() => ({ items: 0 }));
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'zustand' import is restricted from being used by a pattern. Server data: the generated TanStack Query hooks");
    }

    [Fact]
    public void A_client_generated_for_another_state_library_than_tanstack_query_fails_loudly()
    {
        using var project = new GeneratedProject(Profile, "frontend.state=redux-toolkit");
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("The orval pack generates TanStack Query hooks for React only so far");
    }

    private static void RunWeb(GeneratedProject project, params string[] steps)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in steps)
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }
    }
}

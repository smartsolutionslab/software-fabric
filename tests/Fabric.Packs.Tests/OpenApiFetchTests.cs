using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.api-client.openapi-fetch (#264): a typed fetch client over the types openapi-typescript generates from the
/// checked-in web/app/openapi.json, with React and with Vue; the signed-in user's token on every call, an ApiError for
/// every answer outside 2xx, and the same stale-document gate as orval's.
/// </summary>
public sealed class OpenApiFetchTests
{
    private const string OpenApiFetch = "frontend.api-client=openapi-fetch";

    /// <summary>React with TanStack Query (lean-crud's state) and Vue with Pinia and TanStack Query (saas-standard-vue's).</summary>
    public static readonly TheoryData<string, string> Frameworks = new()
    {
        { "lean-crud", "frontend.state=tanstack-query" },
        { "saas-standard-vue", "frontend.state=pinia-tanstack-query" },
    };

    /// <summary>
    /// A test a developer would write: a typed call reaches the host through /api and carries the token oidc-client-ts
    /// stored for the signed-in user; an answer outside 2xx rejects with ApiError. It runs in the generated app's own Vitest.
    /// </summary>
    private const string ClientTest = """
        import { afterEach, expect, test, vi } from 'vitest';
        import { ApiError, api } from '@/api/baseApi';
        import { oidcConfig } from '@/auth/oidcConfig';

        afterEach(() => {
          vi.unstubAllGlobals();
          sessionStorage.clear();
        });

        test('a typed call reaches the host through /api with the signed-in users token', async () => {
          sessionStorage.setItem(
            `oidc.user:${oidcConfig.authority}:${oidcConfig.client_id}`,
            JSON.stringify({ access_token: 'token-of-developer', token_type: 'Bearer', profile: { sub: 'developer' } }),
          );
          const fetch = vi.fn<typeof globalThis.fetch>(() =>
            Promise.resolve(new Response(JSON.stringify({ name: 'developer' }), { status: 200, headers: { 'Content-Type': 'application/json' } })),
          );
          vi.stubGlobal('fetch', fetch);

          const { data } = await api.GET('/me');

          expect(data).toEqual({ name: 'developer' });
          const [input, init] = fetch.mock.calls[0] ?? [];
          const request = new Request(input ?? '', init);
          expect(new URL(request.url).pathname).toBe('/api/me');
          expect(request.headers.get('Authorization')).toBe('Bearer token-of-developer');
        });

        test('an answer outside 2xx rejects with ApiError and its status', async () => {
          vi.stubGlobal('fetch', vi.fn<typeof globalThis.fetch>(() => Promise.resolve(new Response('nobody', { status: 401 }))));

          const call = api.GET('/me');

          await expect(call).rejects.toBeInstanceOf(ApiError);
          await expect(call).rejects.toMatchObject({ status: 401 });
        });
        """;

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void A_project_with_openapi_fetch_generates_from_the_real_packs_alone(string profile, string state)
    {
        foreach (var overrides in new[] { new[] { OpenApiFetch, state }, [OpenApiFetch, state, "backend.auth=none", "backend.tenancy=none"] })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void The_client_is_generated_by_openapi_typescript_from_the_checked_in_document_and_not_checked_in(string profile, string state)
    {
        var files = Files(profile, OpenApiFetch, state);

        files["web/app/package.json"].ShouldContain("\"openapi-fetch\": \"0.17.0\"");
        files["web/app/package.json"].ShouldContain("\"openapi-typescript\": \"7.13.0\"");
        files["web/app/package.json"].ShouldContain("\"codegen\": \"openapi-typescript ./openapi.json --output ./src/api/generated.ts\"");
        files["web/app/package.json"].ShouldContain("\"build\": \"pnpm codegen && ");
        files["web/app/package.json"].ShouldNotContain("orval");
        files["web/app/src/api/.gitignore"].ShouldBe("generated.ts\n");
        files["web/app/src/api/baseApi.ts"].ShouldContain("import type { paths } from './generated';");
        files["web/app/src/api/baseApi.ts"].ShouldContain("import { accessToken } from '@/auth/accessToken';");
        files["web/app/openapi.json"].ShouldContain("\"/me\"");
        files["tests/Integration.Tests/OpenApiDocumentTests.cs"].ShouldContain("The_frontends_copy_of_the_openapi_document_is_current");
        files.Keys.ShouldNotContain("web/app/orval.config.ts");
        files.Keys.ShouldNotContain("web/app/openapi-config.json");
    }

    [Fact]
    public void The_gate_is_orvals_stale_document_test()
    {
        var files = Files("lean-crud", OpenApiFetch);
        var orval = Files("lean-crud");

        files["tests/Integration.Tests/OpenApiDocumentTests.cs"].ShouldBe(orval["tests/Integration.Tests/OpenApiDocumentTests.cs"]);
        files["web/app/openapi.json"].ShouldBe(orval["web/app/openapi.json"]);
        Repository.Packs.Packs["frontend.api-client.openapi-fetch"].Gates.ShouldContain(gate => gate.File == "templates/OpenApiDocumentTests.cs.sbn");
    }

    [Fact]
    public void Without_sign_in_no_header_is_set()
    {
        var files = Files("lean-crud", OpenApiFetch, "backend.auth=none", "backend.tenancy=none");

        files["web/app/src/api/baseApi.ts"].ShouldNotContain("accessToken");
        files["web/app/src/api/baseApi.ts"].ShouldNotContain("Authorization");
    }

    [Fact]
    public void Its_rules_and_adr_say_how_to_call_the_api_and_tanstack_querys_rules_name_no_generated_query_keys()
    {
        foreach (var (profile, state) in new[] { ("lean-crud", "frontend.state=tanstack-query"), ("saas-standard-vue", "frontend.state=pinia-tanstack-query") })
        {
            var files = Files(profile, OpenApiFetch, state);

            files["AGENTS.md"].ShouldContain("## API client: openapi-fetch");
            files["AGENTS.md"].ShouldContain("api.GET('/billing/notes')");
            files["AGENTS.md"].ShouldNotContain("get...QueryKey()");
            files["AGENTS.md"].ShouldNotContain("orval");
            var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-api-client-openapi-fetch.md", StringComparison.Ordinal)).Value;
            adr.ShouldContain("openapi-typescript");
            adr.ShouldContain("web/app/openapi.json");
        }
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void The_web_app_installs_builds_lints_and_passes_its_tests_and_its_format_check_with_a_module(string profile, string state)
    {
        using var project = new GeneratedProject(profile, OpenApiFetch, state);
        project.AddModule("billing");

        RunWeb(project, "install", "build", "lint", "test");
        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, $"pnpm format:check:\n{output}");
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void A_typed_call_carries_the_signed_in_users_token_and_a_failed_one_rejects_with_ApiError(string profile, string state)
    {
        using var project = new GeneratedProject(profile, OpenApiFetch, state);
        project.Write("web/app/src/api/client.test.ts", ClientTest);

        RunWeb(project, "install", "test");
    }

    [Fact]
    public void A_call_to_a_path_the_document_does_not_have_fails_the_build()
    {
        using var project = new GeneratedProject("lean-crud", OpenApiFetch);
        project.Write("web/app/src/api/unknown.ts", """
            import { api } from '@/api/baseApi';

            export const notes = () => api.GET('/billing/notes');
            """);
        RunWeb(project, "install");

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/api/unknown.ts");
        output.ShouldContain("'/billing/notes'");
    }

    [Fact]
    public void The_dashboards_module_keeps_the_routers_placeholder_page()
    {
        var files = Files("saas-ai-react", OpenApiFetch, "frontend.state=tanstack-query");

        files.Keys.ShouldNotContain("web/app/src/dashboards/DashboardEditor.tsx");
        files["web/app/openapi.json"].ShouldContain("\"/dashboards\"");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
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

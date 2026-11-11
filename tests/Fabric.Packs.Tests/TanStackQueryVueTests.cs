using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>frontend.state: tanstack-query</c> in a Vue app (#238, ADR-0006): server data in <c>@tanstack/vue-query</c>'s cache,
/// installed as a plugin, and client state in Vue's own refs, with no store library; React's provider is not generated.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class TanStackQueryVueTests
{
    private const string Profile = "saas-standard-vue";
    private const string TanStackQuery = "frontend.state=tanstack-query";
    private const string Provider = "web/app/src/providers/query.ts";

    [Fact]
    public void A_vue_project_with_tanstack_query_generates_from_the_real_packs_alone()
    {
        foreach (var overrides in new[] { new[] { TanStackQuery }, [TanStackQuery, "frontend.realtime=signalr"], [TanStackQuery, "backend.auth=none", "backend.tenancy=none"] })
        {
            Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides).Errors.ShouldBeEmpty(string.Join(' ', overrides));
        }
    }

    [Fact]
    public void The_vue_app_installs_vue_query_as_a_plugin_and_generates_no_react_file()
    {
        var files = Files(TanStackQuery);

        files[Provider].ShouldContain("import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';");
        files[Provider].ShouldContain("app.use(VueQueryPlugin, { queryClient });");
        files["web/app/src/main.ts"].ShouldContain("import { withQueryClient } from './providers/query';");
        files["web/app/src/main.ts"].ShouldContain("withQueryClient");
        files["web/app/package.json"].ShouldContain("\"@tanstack/vue-query\": \"5.107.1\"");
        files["web/app/package.json"].ShouldNotContain("react");
        files["web/app/package.json"].ShouldNotContain("pinia");
        files["web/app/orval.config.ts"].ShouldContain("client: 'vue-query',");
        files.Keys.ShouldNotContain(path => path.EndsWith(".tsx", StringComparison.Ordinal));
        files.Keys.ShouldNotContain("web/app/src/providers/pinia.ts");
    }

    [Fact]
    public void Its_rules_and_adr_speak_of_vue_and_lint_refuses_pinia_as_well_as_the_other_stores()
    {
        var files = Files(TanStackQuery);

        files["web/app/eslint.config.js"].ShouldContain("'pinia'");
        files["web/app/eslint.config.js"].ShouldContain("'vuex'");
        files["web/app/eslint.config.js"].ShouldNotContain("react-redux");
        files["AGENTS.md"].ShouldContain("## Frontend state: TanStack Query");
        files["AGENTS.md"].ShouldContain("VueQueryPlugin");
        files["AGENTS.md"].ShouldNotContain("useState");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-state-tanstack-query.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("@tanstack/vue-query");
        adr.ShouldContain("src/providers/query.ts");
        adr.ShouldNotContain("React");
    }

    [Fact]
    public void The_vue_web_app_builds_lints_and_passes_its_tests_and_lint_refuses_a_pinia_store()
    {
        using var project = new GeneratedProject(Profile, TanStackQuery);

        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, format);

        project.Write("web/app/src/cart.ts", """
            import { defineStore } from 'pinia';

            export const useCart = defineStore('cart', () => ({}));
            """);

        var (lintExitCode, lint) = Shell.Run(web, "pnpm", "lint");

        lintExitCode.ShouldNotBe(0, lint);
        lint.ShouldContain("'pinia' import is restricted from being used by a pattern. Server data: the generated TanStack Query hooks; client state: Vue refs");
    }

    private static Dictionary<string, string> Files(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

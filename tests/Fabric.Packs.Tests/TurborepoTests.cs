using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.workspace.turborepo (#284): the pnpm workspace with Turborepo running build, lint and test in every package,
/// in dependency order, from a local cache. Owner decisions on #284 mirror Nx's on #283: package-based, local cache
/// only, the build keyed on every VITE_* and REMOTE_* variable, dist and storybook-static as its outputs, turbo 2.11.10.
/// </summary>
public sealed class TurborepoTests
{
    private const string Turborepo = "frontend.workspace=turborepo";

    public static readonly TheoryData<string> Profiles = new() { "saas-standard-react", "saas-standard-vue", "industrial-event-driven-mfe" };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_root_scripts_run_every_package_through_turbo_and_pin_it(string profile)
    {
        var package = Files(profile, Turborepo)["web/package.json"];

        package.ShouldContain("\"build\": \"turbo run build\"");
        package.ShouldContain("\"lint\": \"turbo run lint\"");
        package.ShouldContain("\"test\": \"turbo run test\"");
        package.ShouldContain("\"turbo\": \"2.11.10\"");
        package.ShouldContain("\"packageManager\": \"pnpm@10.0.0\"");
        package.ShouldNotContain("--recursive");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Turbo_caches_build_lint_and_test_locally_builds_dependencies_first_and_never_uses_a_remote_cache(string profile)
    {
        var turbo = Files(profile, Turborepo)["web/turbo.json"];

        turbo.ShouldContain("\"remoteCache\": { \"enabled\": false }");
        turbo.ShouldContain("\"dependsOn\": [\"^build\"]");
        turbo.ShouldContain("\"outputs\": [\"dist/**\", \"storybook-static/**\"]");
        foreach (var task in new[] { "build", "lint", "test" })
        {
            turbo.ShouldContain($"\"{task}\": {{");
        }

        turbo.ShouldNotContain("\"cache\": false");
        turbo.ShouldNotContain("\"extends\"");
    }

    // A build bakes in VITE_* (the sign-in authority) and REMOTE_* (module federation's remote URLs); both are part of its key.
    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_build_is_keyed_on_its_files_its_dependencies_and_every_vite_and_remote_variable(string profile)
    {
        var turbo = Files(profile, Turborepo)["web/turbo.json"];

        turbo.ShouldContain("\"env\": [\"VITE_*\", \"REMOTE_*\"]");
        turbo.ShouldNotContain("\"inputs\"");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_local_cache_is_not_committed(string profile) =>
        Files(profile, Turborepo)["web/.gitignore"].Split('\n').ShouldContain(".turbo/");

    // Turbo hashes what web/.gitignore does not exclude; a build output it could see would change the next build's inputs.
    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_builds_own_outputs_are_not_inputs_of_the_next_build(string profile)
    {
        var ignored = Files(profile, Turborepo)["web/.gitignore"].Split('\n');

        ignored.ShouldContain("dist/");
        ignored.ShouldContain("*.tsbuildinfo");
    }

    // The workspace's packages, CI's steps and everything outside the workspace root are the pnpm workspace's own.
    [Theory]
    [MemberData(nameof(Profiles))]
    public void Only_the_workspace_root_and_its_documentation_differ_from_the_pnpm_workspace(string profile)
    {
        var pnpm = Files(profile);
        var turbo = Files(profile, Turborepo);

        turbo["web/pnpm-workspace.yaml"].ShouldBe(pnpm["web/pnpm-workspace.yaml"]);
        turbo[".github/workflows/ci.yml"].ShouldBe(pnpm[".github/workflows/ci.yml"]);

        string[] own = ["fabric.yaml", "web/package.json", "web/turbo.json", "web/.gitignore", "README.md", "AGENTS.md"];
        var differing = turbo.Keys.Union(pnpm.Keys)
            .Where(path => !own.Contains(path) && !path.StartsWith("docs/adr/", StringComparison.Ordinal))
            .Where(path => !pnpm.TryGetValue(path, out var before) || !turbo.TryGetValue(path, out var after) || before != after)
            .ToList();
        differing.ShouldBeEmpty();
    }

    [Fact]
    public void The_documentation_names_turborepo_its_cache_and_no_remote_cache()
    {
        var files = Files("saas-standard-react", Turborepo);

        files["AGENTS.md"].ShouldContain("## Frontend workspace: Turborepo");
        files["AGENTS.md"].ShouldContain("turbo run");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-workspace-turborepo.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("remoteCache");
        adr.ShouldContain(".turbo/cache");
    }

    /// <summary>
    /// What a developer sees: the workspace installs, builds, lints and tests through Turborepo, and a second build with
    /// nothing changed is read from the local cache instead of running again.
    /// </summary>
    [Fact]
    public void A_second_build_with_nothing_changed_is_read_from_the_local_cache()
    {
        using var project = new GeneratedProject("saas-standard-react", Turborepo);
        var web = Path.Combine(project.ProjectDirectory, "web");

        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (againExitCode, againOutput) = Shell.Run(web, "pnpm", "build");
        againExitCode.ShouldBe(0, $"pnpm build, again:\n{againOutput}");
        againOutput.ShouldContain("FULL TURBO", customMessage: againOutput);
    }

    /// <summary>
    /// The finding Nx's review raised on #372, held for Turborepo from the start: a rebuild against another sign-in
    /// authority, with no file changed, must build a bundle that signs in there, not replay the previous one from the cache.
    /// </summary>
    [Fact]
    public void A_build_against_another_sign_in_authority_is_not_read_from_the_cache()
    {
        using var project = new GeneratedProject("saas-standard-react", Turborepo);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        foreach (var authority in new[] { "https://first.example/realms/shop", "https://second.example/realms/shop" })
        {
            var (exitCode, output) = Shell.Run(web, new Dictionary<string, string> { ["VITE_OIDC_AUTHORITY"] = authority }, "pnpm", "build");
            exitCode.ShouldBe(0, $"pnpm build with {authority}:\n{output}");
        }

        var bundle = string.Concat(Directory.GetFiles(Path.Combine(web, "app", "dist"), "*.js", SearchOption.AllDirectories).Select(File.ReadAllText));
        bundle.ShouldContain("https://second.example/realms/shop", customMessage: "the second build's bundle signs in at the second authority");
    }

    /// <summary>
    /// With tokens-and-components the ui package builds Storybook into storybook-static/; a build read from the cache
    /// restores it.
    /// </summary>
    [Fact]
    public void A_build_read_from_the_cache_restores_the_storybook()
    {
        using var project = new GeneratedProject("saas-standard-react", Turborepo, "frontend.design-system=tokens-and-components");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var storybook = Path.Combine(web, "packages", "ui", "storybook-static");
        foreach (var step in new[] { "install", "build" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        File.Exists(Path.Combine(storybook, "index.html")).ShouldBeTrue("the first build writes the storybook");
        Directory.Delete(storybook, recursive: true);

        var (againExitCode, againOutput) = Shell.Run(web, "pnpm", "build");
        againExitCode.ShouldBe(0, $"pnpm build, again:\n{againOutput}");
        againOutput.ShouldContain("FULL TURBO", customMessage: againOutput);
        File.Exists(Path.Combine(storybook, "index.html")).ShouldBeTrue("the cached build restores the storybook");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty(string.Join(' ', overrides));
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

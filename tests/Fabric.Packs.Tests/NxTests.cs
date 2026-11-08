using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.workspace.nx (#283): the pnpm workspace with Nx running build, lint and test in every package, in dependency
/// order, from a local cache. Owner decisions on #283: package-based (no plugins), never Nx Cloud, nx 24.0.5.
/// </summary>
public sealed class NxTests
{
    private const string Nx = "frontend.workspace=nx";

    public static readonly TheoryData<string> Profiles = new() { "saas-standard-react", "saas-standard-vue", "industrial-event-driven-mfe" };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_root_scripts_run_every_package_through_nx_and_pin_it(string profile)
    {
        var package = Files(profile, Nx)["web/package.json"];

        package.ShouldContain("\"build\": \"nx run-many -t build\"");
        package.ShouldContain("\"lint\": \"nx run-many -t lint\"");
        package.ShouldContain("\"test\": \"nx run-many -t test\"");
        package.ShouldContain("\"nx\": \"24.0.5\"");
        package.ShouldContain("\"packageManager\": \"pnpm@10.0.0\"");
        package.ShouldNotContain("--recursive");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Nx_caches_build_lint_and_test_locally_builds_dependencies_first_and_never_connects_to_the_cloud(string profile)
    {
        var nx = Files(profile, Nx)["web/nx.json"];

        nx.ShouldContain("\"neverConnectToCloud\": true");
        nx.ShouldContain("\"dependsOn\": [\"^build\"]");
        nx.ShouldContain("\"outputs\": [\"{projectRoot}/dist\"]");
        foreach (var target in new[] { "build", "lint", "test" })
        {
            nx.ShouldContain($"\"{target}\": {{");
        }

        nx.Split("\"cache\": true").Length.ShouldBe(4, "build, lint and test are cached");
        nx.ShouldNotContain("plugins");
        nx.ShouldNotContain("nxCloud");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_local_cache_is_not_committed(string profile) =>
        Files(profile, Nx)["web/.gitignore"].ShouldContain(".nx/");

    // Nx hashes what web/.gitignore does not exclude; a build output it could see would change the next build's inputs.
    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_builds_own_outputs_are_not_inputs_of_the_next_build(string profile)
    {
        var ignored = Files(profile, Nx)["web/.gitignore"].Split('\n');

        ignored.ShouldContain("dist/");
        ignored.ShouldContain("*.tsbuildinfo");
    }

    // The workspace's packages, CI's steps and everything outside the workspace root are the pnpm workspace's own.
    [Theory]
    [MemberData(nameof(Profiles))]
    public void Only_the_workspace_root_and_its_documentation_differ_from_the_pnpm_workspace(string profile)
    {
        var pnpm = Files(profile);
        var nx = Files(profile, Nx);

        nx["web/pnpm-workspace.yaml"].ShouldBe(pnpm["web/pnpm-workspace.yaml"]);
        nx[".github/workflows/ci.yml"].ShouldBe(pnpm[".github/workflows/ci.yml"]);

        string[] own = ["fabric.yaml", "web/package.json", "web/nx.json", "web/.gitignore", "README.md", "AGENTS.md"];
        var differing = nx.Keys.Union(pnpm.Keys)
            .Where(path => !own.Contains(path) && !path.StartsWith("docs/adr/", StringComparison.Ordinal))
            .Where(path => !pnpm.TryGetValue(path, out var before) || !nx.TryGetValue(path, out var after) || before != after)
            .ToList();
        differing.ShouldBeEmpty();
    }

    [Fact]
    public void The_documentation_names_nx_its_cache_and_no_cloud()
    {
        var files = Files("saas-standard-react", Nx);

        files["AGENTS.md"].ShouldContain("## Frontend workspace: Nx");
        files["AGENTS.md"].ShouldContain("nx run-many");
        var adr = files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith("-frontend-workspace-nx.md", StringComparison.Ordinal)).Value;
        adr.ShouldContain("neverConnectToCloud");
        adr.ShouldContain(".nx/cache");
    }

    /// <summary>
    /// What a developer sees: the workspace installs, builds, lints and tests through Nx, and a second build with nothing
    /// changed is read from the local cache instead of running again.
    /// </summary>
    [Fact]
    public void A_second_build_with_nothing_changed_is_read_from_the_local_cache()
    {
        using var project = new GeneratedProject("saas-standard-react", Nx);
        var web = Path.Combine(project.ProjectDirectory, "web");

        foreach (var step in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (againExitCode, againOutput) = Shell.Run(web, "pnpm", "build");
        againExitCode.ShouldBe(0, $"pnpm build, again:\n{againOutput}");
        againOutput.ShouldContain("1/1 hit (100%)", customMessage: againOutput);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty(string.Join(' ', overrides));
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}

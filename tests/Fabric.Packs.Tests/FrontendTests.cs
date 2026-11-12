using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

public sealed class FrontendTests
{
    private const string Profile = "saas-standard-react";

    [Fact]
    public void The_generated_web_app_installs_builds_lints_passes_its_tests_and_leaves_the_working_tree_clean()
    {
        using var project = new GeneratedProject(Profile);
        var web = Path.Combine(project.ProjectDirectory, "web");

        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        // The generated project with its lockfile, as a developer commits it after the first install.
        Git(project.ProjectDirectory, "init", "--quiet");
        Git(project.ProjectDirectory, "add", "--all");
        Git(project.ProjectDirectory, "-c", "user.name=fabric", "-c", "user.email=fabric@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "generated");

        foreach (var step in new[] { "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        // An agent's clean-tree check runs right after these steps: whatever they write must be ignored, not left untracked.
        Git(project.ProjectDirectory, "status", "--porcelain", "--untracked-files=all").ShouldBeEmpty();
    }

    private static string Git(string directory, params string[] arguments)
    {
        var (exitCode, output) = Shell.Run(directory, "git", arguments);
        exitCode.ShouldBe(0, $"git {string.Join(' ', arguments)}:\n{output}");
        return output.Trim();
    }

    [Fact]
    public void The_generated_web_app_passes_its_format_check_and_a_formatting_violation_fails_it()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");

        // What CI has on disk when it checks formatting: the generated client and the build output included.
        foreach (var step in new[] { "install", "build" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (cleanExitCode, cleanOutput) = Shell.Run(app, "pnpm", "format:check");
        cleanExitCode.ShouldBe(0, $"pnpm format:check on the untouched project:\n{cleanOutput}");

        project.Write("web/app/src/unformatted.ts", "export const greeting = \"hello\"\n");

        var (exitCode, output) = Shell.Run(app, "pnpm", "format:check");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/unformatted.ts");
    }

    [Fact]
    public void A_test_that_waits_for_a_count_instead_of_a_condition_fails_lint()
    {
        using var project = new GeneratedProject(Profile);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        // Waiting on the condition, and driving fake timers forward, are what a test should do; lint accepts them.
        project.Write("web/app/src/waits/condition.test.tsx", ConditionTest);
        var (cleanExitCode, cleanOutput) = Shell.Run(web, "pnpm", "lint");
        cleanExitCode.ShouldBe(0, $"pnpm lint with a test that waits for its condition:\n{cleanOutput}");

        project.Write("web/app/src/waits/counted.test.tsx", CountedTest);
        project.Write("web/app/src/waits/settled.test.tsx", SettledTest);

        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldNotContain("condition.test.tsx");
        output.ShouldContain("counted.test.tsx");
        output.ShouldContain("settled.test.tsx");
        output.ShouldContain("A fixed count of timer yields is not a bound on work that advances in another phase of the event loop");
        output.ShouldContain("A settle may not immediately precede an assertion");
        output.ShouldContain("6 problems (6 errors, 0 warnings)");
    }

    /// <summary>A test that waits for what it asserts: lint has nothing to say about it.</summary>
    private const string ConditionTest = """
        import { waitFor } from '@testing-library/react';
        import { afterEach, expect, test, vi } from 'vitest';

        afterEach(() => {
          vi.useRealTimers();
        });

        test('waits for the condition', async () => {
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 10);

          await waitFor(() => expect(ready).toBe(true));
        });

        test('drives fake timers forward', async () => {
          vi.useFakeTimers();
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 100);

          await vi.advanceTimersByTimeAsync(100);
          expect(ready).toBe(true);
        });
        """;

    /// <summary>Three loops that yield to the timer a fixed number of times: a literal bound, a named bound, a fixed array.</summary>
    private const string CountedTest = """
        import { expect, test } from 'vitest';

        const ROUNDS = 10;

        test('a literal count of timer yields', async () => {
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 0);
          for (let i = 0; i < 10; i += 1) {
            await new Promise((resolve) => setTimeout(resolve, 0));
          }
          expect(ready).toBe(true);
        });

        test('a named count of timer yields', async () => {
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 0);
          for (let i = 0; i < ROUNDS; i += 1) {
            await new Promise((resolve) => setTimeout(resolve, 0));
          }
          expect(ready).toBe(true);
        });

        test('a timer yield per element of a fixed array', async () => {
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 0);
          for (const delay of [0, 0, 0]) {
            await new Promise((resolve) => setTimeout(resolve, delay));
          }
          expect(ready).toBe(true);
        });
        """;

    /// <summary>Three settles immediately before an assertion: a raw timer promise, a sleep helper, an act with nothing in it.</summary>
    private const string SettledTest = """
        import { act } from '@testing-library/react';
        import { expect, test } from 'vitest';

        const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

        test('a raw timer promise before the assertion', async () => {
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 0);
          await new Promise((resolve) => setTimeout(resolve, 50));
          expect(ready).toBe(true);
        });

        test('a sleep before the assertion', async () => {
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 0);
          await sleep(50);
          expect(ready).toBe(true);
        });

        test('an empty act before the assertion', async () => {
          let ready = false;
          setTimeout(() => {
            ready = true;
          }, 0);
          await act(async () => {});
          expect(ready).toBe(true);
        });
        """;

    [Fact]
    public void The_editorconfig_indents_web_files_as_the_prettier_config_does_and_csharp_with_four_spaces()
    {
        using var project = new GeneratedProject(Profile);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        // Prettier resolves both files itself: its own config with .editorconfig ignored, and .editorconfig alone
        // (an empty config file in place of .prettierrc.json).
        project.Write("indent-probe/empty.json", "{}");
        project.Write("web/app/indent-probe.mjs", IndentProbe);
        string[] files = ["src/App.tsx", "index.html", "eslint.config.js", "package.json", "../../src/Host/Program.cs"];
        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), "node", ["indent-probe.mjs", .. files]);
        exitCode.ShouldBe(0, output);

        var widths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .ToDictionary(fields => fields[0], fields => (Prettier: fields[1], EditorConfig: fields[2]));
        foreach (var file in files[..^1])
        {
            widths[file].EditorConfig.ShouldBe(widths[file].Prettier, $"{file}: .editorconfig indent vs .prettierrc.json tabWidth");
        }

        widths["src/App.tsx"].Prettier.ShouldBe("2");
        widths["../../src/Host/Program.cs"].EditorConfig.ShouldBe("4");
    }

    /// <summary>Prints "file prettier-tabWidth editorconfig-tabWidth" for each argument.</summary>
    private const string IndentProbe = """
        import { fileURLToPath } from 'node:url';
        import * as prettier from 'prettier';

        const empty = fileURLToPath(new URL('../../indent-probe/empty.json', import.meta.url));
        for (const file of process.argv.slice(2)) {
          const own = await prettier.resolveConfig(file, { editorconfig: false });
          const editorconfig = await prettier.resolveConfig(file, { editorconfig: true, config: empty });
          console.log(file, own?.tabWidth, editorconfig?.tabWidth);
        }
        """;

    [Fact]
    public void The_generated_ci_checks_formatting_after_installing_the_workspace()
    {
        using var project = new GeneratedProject(Profile);
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));

        ci.ShouldContain("      - run: pnpm format:check\n        working-directory: web/app\n");
        ci.IndexOf("run: pnpm format:check", StringComparison.Ordinal)
            .ShouldBeGreaterThan(ci.IndexOf("run: pnpm install --frozen-lockfile", StringComparison.Ordinal));
    }

    [Fact]
    public void The_app_host_runs_the_web_app_with_aspire()
    {
        using var project = new GeneratedProject(Profile);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "AppHost", "AppHost.cs")).ShouldContain("AddViteApp(\"web\", \"../../web/app\")");
        project.Build();
    }

    [Fact]
    public void The_generated_ci_runs_the_end_to_end_tests_against_the_running_application()
    {
        using var project = new GeneratedProject(Profile);
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));

        ci.ShouldContain("\n  e2e:\n");
        var e2e = ci[ci.IndexOf("\n  e2e:\n", StringComparison.Ordinal)..];
        e2e.ShouldContain("playwright install --with-deps chromium");
        e2e.ShouldContain("run: bash .github/scripts/e2e.sh");
        File.Exists(Path.Combine(project.ProjectDirectory, ".github", "scripts", "e2e.sh")).ShouldBeTrue();
    }

    [Fact]
    public void The_lockfile_is_the_first_step_and_ci_installs_only_from_it()
    {
        using var project = new GeneratedProject(Profile);
        var readme = File.ReadAllText(Path.Combine(project.ProjectDirectory, "README.md"));
        var ci = File.ReadAllText(Path.Combine(project.ProjectDirectory, ".github", "workflows", "ci.yml"));

        readme.ShouldContain("## First steps");
        readme[readme.IndexOf("## First steps", StringComparison.Ordinal)..].ShouldContain("pnpm install");
        ci.ShouldContain("web/pnpm-lock.yaml is missing");
        ci.ShouldNotContain("run: pnpm install\n");
        ci.ShouldContain("run: pnpm install --frozen-lockfile");
    }

    public static TheoryData<string, string> ProfilesAndTestExtras { get; } = new()
    {
        { "saas-standard-react", "" },
        { "saas-standard-react", "screenshot-tests" },
        { "saas-standard-vue", "" },
        { "saas-standard-vue", "screenshot-tests" },
    };

    /// <summary>Prettier ends a file with exactly one newline, so a web file ending in a blank line fails <c>format:check</c>.</summary>
    [Theory]
    [MemberData(nameof(ProfilesAndTestExtras))]
    public void Without_the_accessibility_extra_no_web_file_ends_with_a_blank_line(string profile, string extras)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, [$"frontend.test.extras={extras}"]);
        generated.Errors.ShouldBeEmpty();

        generated.Files
            .Where(file => file.Path.StartsWith("web/", StringComparison.Ordinal) && file.Content.EndsWith("\n\n", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ShouldBeEmpty();
    }
}

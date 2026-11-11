namespace Fabric.Packs.Tests;

/// <summary>
/// A generated web app sets skipLibCheck, which skips every declaration file, the ones the packs generate included: that
/// hid #556's type error, and its tags became an error type that took any property (#560). The app's build type-checks
/// the workspace's own declaration files, the shell's and every remote's, and still skips those under node_modules.
/// </summary>
public sealed class DeclarationFilesTests
{
    private const string WebComponents = "frontend.design-system=web-components";

    /// <summary>#556's declarations: inside namespace JSX, Element is JSX.Element, which takes no type arguments.</summary>
    private const string BrokenDesignSystemDeclarations = """
        import type { DetailedHTMLProps, HTMLAttributes } from 'react';
        import type { Badge, BadgeTone } from '@shop/ui';

        type Element<T extends HTMLElement, Properties> = DetailedHTMLProps<HTMLAttributes<T>, T> & Partial<Properties>;

        declare module 'react' {
          namespace JSX {
            interface IntrinsicElements {
              'ds-badge': Element<Badge, { tone: BadgeTone }>;
            }
          }
        }

        """;

    [Fact]
    public void A_type_error_in_the_design_systems_declarations_fails_the_react_build()
    {
        using var project = new GeneratedProject("saas-standard-react", WebComponents);
        project.Write("web/app/src/design-system.d.ts", BrokenDesignSystemDeclarations);
        Install(project);

        var (exitCode, output) = Build(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/design-system.d.ts");
        output.ShouldContain("TS2315");
    }

    [Fact]
    public void A_type_error_in_the_vue_apps_declarations_fails_its_build()
    {
        using var project = new GeneratedProject("saas-standard-vue");
        project.Write("web/app/src/vite-env.d.ts", """
            /// <reference types="vite/client" />

            interface ImportMetaEnv {
              readonly VITE_GREETING: Greeting;
            }

            """);
        Install(project);

        var (exitCode, output) = Build(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/vite-env.d.ts");
        output.ShouldContain("Cannot find name 'Greeting'");
    }

    [Fact]
    public void A_type_error_in_a_module_declaration_file_fails_the_react_build()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.Write("web/app/src/greeting.d.mts", "export type Greeting = Missing;\n");
        Install(project);

        var (exitCode, output) = Build(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/greeting.d.mts");
        output.ShouldContain("Cannot find name 'Missing'");
    }

    // The check is Vue-aware, as vue-tsc is: a declaration file may name a single-file component's type.
    [Fact]
    public void A_declaration_file_that_imports_a_vue_component_builds()
    {
        using var project = new GeneratedProject("saas-standard-vue");
        project.Write("web/app/src/components.d.ts", """
            import App from './App.vue';

            export type AppInstance = InstanceType<typeof App>;

            """);
        Install(project);

        var (exitCode, output) = Build(project);

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void A_type_error_in_a_remotes_declarations_fails_the_build()
    {
        using var project = new GeneratedProject(
            "saas-standard-react",
            "frontend.architecture=micro-frontends",
            WebComponents,
            "frontend.mfe-technique=module-federation");
        project.AddModule("billing");
        project.Write("web/modules/billing/src/design-system.d.ts", BrokenDesignSystemDeclarations);
        Install(project);

        var (exitCode, output) = Build(project);

        exitCode.ShouldNotBe(0, output);
        // The remote's own build names its file relative to the remote; the shell's copy is intact.
        output.ShouldContain("src/design-system.d.ts");
        output.ShouldContain("TS2315");
    }

    // Each remote checks its own declarations: Nx and Turborepo can restore the shell's build from their cache when only a
    // remote changed, since a remote is not one of the shell's dependencies.
    [Theory]
    [InlineData("module-federation", "react")]
    [InlineData("native-federation", "react")]
    [InlineData("native-federation", "vue")]
    [InlineData("web-components", "react")]
    [InlineData("web-components", "vue")]
    public void A_type_error_in_a_remotes_declarations_fails_the_remotes_own_build(string technique, string framework)
    {
        using var project = new GeneratedProject(
            $"saas-standard-{framework}",
            "frontend.architecture=micro-frontends",
            "frontend.design-system=tokens",
            $"frontend.mfe-technique={technique}");
        project.AddModule("billing");
        project.Write("web/modules/billing/src/broken.d.ts", "export type Broken = Missing;\n");
        Install(project);

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "modules", "billing"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/broken.d.ts");
        output.ShouldContain("Cannot find name 'Missing'");
    }

    // A declaration file outside the tsconfig's include joins the compilation when a source file imports it.
    [Theory]
    [InlineData("module-federation", "react")]
    [InlineData("native-federation", "vue")]
    public void A_type_error_in_a_declaration_file_a_remote_imports_fails_the_remotes_own_build(string technique, string framework)
    {
        using var project = new GeneratedProject(
            $"saas-standard-{framework}",
            "frontend.architecture=micro-frontends",
            "frontend.design-system=tokens",
            $"frontend.mfe-technique={technique}");
        project.AddModule("billing");
        project.Write("web/modules/billing/types/broken.d.ts", "export type Broken = Missing;\n");
        project.Write("web/modules/billing/src/uses.ts", "export type { Broken } from '../types/broken';\n");
        Install(project);

        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "modules", "billing"), "pnpm", "build");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("types/broken.d.ts");
        output.ShouldContain("Cannot find name 'Missing'");
    }

    // Over a megabyte of errors under node_modules comes before the app's own: the check reads all of the compiler's output.
    [Fact]
    public void A_type_error_in_the_apps_declarations_fails_the_build_after_a_megabyte_of_dependency_errors()
    {
        using var project = new GeneratedProject("saas-standard-react");
        Install(project);
        var declarations = string.Concat(Enumerable.Range(0, 30000).Select(index => $"export type Broken{index} = Missing{index};\n"));
        project.Write("web/app/node_modules/broken-types/package.json", """{ "name": "broken-types", "types": "index.d.ts" }""" + "\n");
        project.Write("web/app/node_modules/broken-types/index.d.ts", declarations);
        project.Write("web/app/src/a-uses.ts", "export type { Broken0 } from 'broken-types';\n");
        project.Write("web/app/src/zz-broken.d.ts", "export type Broken = Missing;\n");

        var (exitCode, output) = Build(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/zz-broken.d.ts");
    }

    [Fact]
    public void A_type_error_in_a_declaration_file_under_node_modules_still_builds()
    {
        using var project = new GeneratedProject("saas-standard-react");
        Install(project);
        project.Write("web/app/node_modules/broken-types/package.json", """{ "name": "broken-types", "types": "index.d.ts" }""" + "\n");
        project.Write("web/app/node_modules/broken-types/index.d.ts", "export type Broken = Missing;\n");
        project.Write("web/app/src/broken.d.ts", "import type { Broken } from 'broken-types';\n\nexport type Uses = Broken;\n");

        var (exitCode, output) = Build(project);

        exitCode.ShouldBe(0, output);
    }

    private static void Install(GeneratedProject project)
    {
        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "install");
        exitCode.ShouldBe(0, $"pnpm install:\n{output}");
    }

    private static (int ExitCode, string Output) Build(GeneratedProject project) =>
        Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "build");
}

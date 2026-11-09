using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>web.custom-element-prefixes</c>, declared by the Vue framework pack (#388): tag prefixes Vue's compiler leaves to the
/// browser as custom elements (<c>isCustomElement</c>), so a pack of web components (#280) can use its tags in templates.
/// Without a contribution <c>vite.config.ts</c> is what it always was.
/// </summary>
public sealed class VueCustomElementsSlotTests
{
    private const string Profile = "saas-standard-vue";
    private const string ViteConfig = "web/app/vite.config.ts";

    /// <summary>A component using a tag another pack declares as a custom element.</summary>
    private const string ProbeHost = """
        <template>
          <probe-widget label="Probe" />
        </template>
        """;

    /// <summary>Vue warns when it resolves a tag as a component and finds none; a custom element it leaves alone.</summary>
    private const string ProbeHostTest = """
        import { render } from '@testing-library/vue';
        import { expect, test, vi } from 'vitest';
        import ProbeHost from './ProbeHost.vue';

        test('a tag with a declared prefix is left to the browser as a custom element', () => {
          const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

          const { container } = render(ProbeHost);

          expect(container.querySelector('probe-widget')?.getAttribute('label')).toBe('Probe');
          expect(warn).not.toHaveBeenCalled();
        });
        """;

    [Fact]
    public void A_prefix_another_pack_contributes_becomes_vues_is_custom_element()
    {
        using var probe = Probe();
        var packs = Repository.Packs.Packs.ToDictionary();
        packs[probe.Manifest.Option] = probe.Manifest;

        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, ["frontend.pwa=yes"]);
        generated.Errors.ShouldBeEmpty();

        var config = generated.Files.Single(file => file.Path == ViteConfig).Content;
        config.ShouldContain("""
            const host = process.env.services__host__http__0;

            // Tags Vue's compiler leaves to the browser as custom elements: any starting with a prefix a pack declares.
            const customElementPrefixes = ['probe-'];
            const isCustomElement = (tag: string) => customElementPrefixes.some((prefix) => tag.startsWith(prefix));

            export default defineConfig({
              plugins: [
                // Vue's first, then the chosen packs' in order.
                vue({ template: { compilerOptions: { isCustomElement } } }),

            """);
    }

    [Fact]
    public void Without_a_contribution_vue_takes_no_compiler_options()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, []);
        generated.Errors.ShouldBeEmpty();

        var config = generated.Files.Single(file => file.Path == ViteConfig).Content;
        config.ShouldContain("    vue(),\n");
        config.ShouldNotContain("isCustomElement");
        config.ShouldNotContain("customElementPrefixes");
    }

    [Fact]
    public void With_a_contribution_the_vue_web_app_builds_passes_its_format_check_and_leaves_the_tag_to_the_browser()
    {
        using var probe = Probe();
        using var project = new GeneratedProject(Profile, [probe.Manifest], "frontend.pwa=yes");
        project.Write("web/app/src/components/ProbeHost.vue", ProbeHost + "\n");
        project.Write("web/app/src/components/ProbeHost.test.ts", ProbeHostTest + "\n");
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (formatExitCode, format) = Shell.Run(Path.Combine(web, "app"), "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{format}");
    }

    /// <summary>Stands in for frontend.pwa.yes, contributing only the prefix of its tags.</summary>
    private static ProbePack Probe() => new("frontend.pwa.yes", new Dictionary<string, string>
    {
        ["web.custom-element-prefixes"] = "'probe-'",
    });
}

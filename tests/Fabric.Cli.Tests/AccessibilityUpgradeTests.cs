namespace Fabric.Cli.Tests;

/// <summary>
/// <c>fabric upgrade</c> of a single-page app made before the accessibility pack generated a test per module screen
/// (#604), whose own component test of a screen already checks it with axe, as the generated AGENTS.md asks (#627).
/// The old generator is this build with the real packs minus that pack's per-module files.
/// </summary>
public sealed class AccessibilityUpgradeTests : IDisposable
{
    private const string OldVersion = "0.1.0";

    private const string OwnPageTest = "web/app/src/modules/billing/BillingPage.test.tsx";

    private const string OwnPageTestSource = """
        import { render } from '@testing-library/react';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import { BillingPage } from './BillingPage';

        test('the billing page has no accessibility violations axe can find', async () => {
          const { container } = render(<BillingPage />);

          expect(await axe(container)).toHaveNoViolations();
        });

        """;

    private readonly string root = Directory.CreateTempSubdirectory("fabric-upgrade-axe-").FullName;

    private readonly string project;

    private readonly string oldFabric;

    public AccessibilityUpgradeTests()
    {
        project = Path.Combine(root, "shop");
        var oldPacks = Path.Combine(root, "packs");
        CopyDirectory(Paths.RepositoryPacks, oldPacks);
        var accessibility = Path.Combine(oldPacks, "frontend", "test", "extras", "accessibility", "pack.yaml");
        var lines = File.ReadAllLines(accessibility);
        List<string> withoutModuleTests = [.. lines.Where(line => !line.Contains("per-module: true", StringComparison.Ordinal))];
        withoutModuleTests.Count.ShouldBeLessThan(lines.Length);
        File.WriteAllLines(accessibility, withoutModuleTests);

        oldFabric = OldFabric.Create(root, OldVersion, Paths.RepositoryCatalog, oldPacks);
        Run(oldFabric, ["new", project, "--profile", "saas-standard-react"]);
        Run(oldFabric, ["add-module", "billing", "--project", project]);
        Git.Run(project, "init", "-q");
        Git.Commit(project, "generate with fabric 0.1.0");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_screen_whose_own_component_test_checks_it_with_axe_upgrades_with_no_conflict_on_that_test()
    {
        File.Exists(Path.Combine(project, OwnPageTest)).ShouldBeFalse();
        File.WriteAllText(Path.Combine(project, OwnPageTest), OwnPageTestSource);
        Git.Commit(project, "test the billing page");

        var run = CliRun.Of("upgrade", "--project", project, "--from", oldFabric, "--catalog", Paths.RepositoryCatalog, "--packs", Paths.RepositoryPacks);

        run.ExitCode.ShouldBe(0, run.Error);
        Git.Run(project, "ls-files", "-u").ShouldBeEmpty();
        run.Output.ShouldNotContain("conflict");
        File.ReadAllText(Path.Combine(project, OwnPageTest)).ShouldBe(OwnPageTestSource);
        run.Output.ShouldContain("  A web/app/src/modules/billing/BillingPage.axe.test.tsx");
    }

    private static void Run(string fabric, string[] arguments)
    {
        var (exitCode, output, error) = Programs.Run(fabric, Path.GetDirectoryName(fabric)!, arguments);
        exitCode.ShouldBe(0, output + error);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }
}

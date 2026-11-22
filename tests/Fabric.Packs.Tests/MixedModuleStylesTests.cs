namespace Fabric.Packs.Tests;

/// <summary>
/// Modules of different styles in one project (#848, ADR-0005's amendment of 2026-11-20): DDD with strict depth,
/// event-sourced on Marten and CRUD, in one services project, regenerated from <c>fabric.yaml</c> as <c>fabric upgrade</c>
/// writes it. Each style's project-wide files and slots are written once, and the project builds and passes its
/// architecture tests and every module's own.
/// </summary>
public sealed class MixedModuleStylesTests
{
    [Fact]
    public void Ddd_strict_event_sourced_on_marten_and_crud_modules_in_one_services_project_build_and_pass_their_tests()
    {
        using var project = new GeneratedProject("saas-services-vue", "module.ddd-depth=strict");
        project.Regenerate(
            ("organisation", []),
            ("learning", ["module.style=event-sourced", "module.event-store=marten", "module.cqrs=logical"]),
            ("catalog", ["module.style=crud"]));

        project.Build();
        foreach (var tests in new[]
        {
            "tests/Architecture.Tests/Architecture.Tests.csproj",
            "tests/Modules/Organisation.Tests/Organisation.Tests.csproj",
            "tests/Modules/Learning.Tests/Learning.Tests.csproj",
            "tests/Modules/Catalog.Tests/Catalog.Tests.csproj",
        })
        {
            var (exitCode, output) = project.Test(tests);
            exitCode.ShouldBe(0, output);
            output.ShouldContain("failed: 0");
        }
    }
}

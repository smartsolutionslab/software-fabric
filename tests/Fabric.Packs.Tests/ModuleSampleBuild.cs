namespace Fabric.Packs.Tests;

/// <summary>
/// A new module's sample (#734) in a built project, for the variants no profile's CI builds (the profiles' runs build
/// ddd, strict ddd, crud and event-sourced with physical CQRS, and run the sample's integration tests with the rest):
/// the module's own tests and the architecture rules pass with it, and through the AppHost a signed-in user creates a
/// sample with <c>POST /billing/samples</c> and finds it in a page of <c>GET /billing/samples</c>, with the frontend's
/// copy of the OpenAPI document current. One class per variant, so the test shards can run them apart.
/// </summary>
internal static class ModuleSampleBuild
{
    public static void PassesItsTestsAndRunsThroughTheAppHost(string profile, params string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test",
            "--project",
            "tests/Integration.Tests/Integration.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--no-ansi",
            "--filter-class",
            "Shop.Integration.Tests.BillingSamplesTests",
            "--filter-class",
            "Shop.Integration.Tests.OpenApiDocumentTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
    }
}

using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>The base's ServiceDefaults: every HttpClient retries only idempotent methods unless a client opts back in.</summary>
public sealed partial class ServiceDefaultsTests
{
    private const string Profile = "saas-standard-react";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    [Fact]
    public void A_default_http_client_retries_a_get_but_not_a_post_and_a_client_can_opt_back_in()
    {
        using var project = new GeneratedProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_retry_rule_fails_a_project_whose_defaults_retry_every_method()
    {
        using var project = new GeneratedProject(Profile);
        var extensions = Path.Combine(project.ProjectDirectory, "src", "ServiceDefaults", "Extensions.cs");
        var retryingEveryMethod = StandardResilienceHandler().Replace(File.ReadAllText(extensions), "AddStandardResilienceHandler()");
        project.Write("src/ServiceDefaults/Extensions.cs", retryingEveryMethod);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("A POST that failed with ServiceUnavailable was attempted 4 times by a default HttpClient; expected 1.");
    }

    [GeneratedRegex(@"AddStandardResilienceHandler\([^)]*\)")]
    private static partial Regex StandardResilienceHandler();
}

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.topology: services</c> with <c>backend.auth: none</c> (#816): the edge maps no path of its own, so every
/// operation in its document is a service's, retagged to the edge's name. The document must then list that tag, as a
/// modular monolith's host lists its own, or the frontend's generated copy is stale on a fresh project.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ServicesWithoutAuthOpenApiTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("sse")]
    [InlineData("signalr")]
    public void The_edges_document_is_the_frontends_generated_copy_without_rewriting_it(string realtime)
    {
        string[] overrides = realtime == "none"
            ? ["backend.topology=services", "backend.auth=none", "backend.tenancy=none"]
            : ["backend.topology=services", "backend.auth=none", "backend.tenancy=none", "backend.bus.transport=rabbitmq", $"frontend.realtime={realtime}"];
        using var project = new GeneratedProject("saas-standard-react", overrides);
        project.AddModule("billing");
        var generated = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "openapi.json"));
        project.Build();

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
            "Shop.Integration.Tests.OpenApiDocumentTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldNotContain("succeeded: 0");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "openapi.json")).ShouldBe(generated);
    }
}

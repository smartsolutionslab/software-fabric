using System.Text.RegularExpressions;
using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The AppHost's build compiles every project it runs, so Aspire starts each with <c>dotnet run --no-build</c>; a
/// project added by path is rebuilt at every start, which made a services project's integration fixture miss its start-up
/// limit (#872). The generated CI's jobs end at a timeout of their own, not GitHub's six hours.
/// </summary>
public sealed partial class AppHostRunsItsOwnBuildTests
{
    private const string Monolith = "saas-standard-react";

    private const string Services = "saas-services-vue";

    private const string Rule = "tests/Architecture.Tests/Rules/TheAppHostRunsWhatItBuilt.cs";

    [Theory]
    [InlineData(Monolith)]
    [InlineData(Services)]
    public void No_AppHost_source_adds_a_project_by_path_but_AddBuiltProject(string profile)
    {
        var files = WithModules(profile, "billing", "shipping");

        var byPath = files
            .Where(file => file.Key.StartsWith("src/AppHost/", StringComparison.Ordinal) && file.Key.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => file.Key != "src/AppHost/Wiring/BuiltProjects.cs" && ByPath().IsMatch(file.Value))
            .Select(file => file.Key)
            .ToList();
        byPath.ShouldBeEmpty();
        files.Keys.ShouldContain(Rule);
    }

    [Fact]
    public void A_services_AppHost_adds_the_edge_and_every_service_with_AddBuiltProject()
    {
        var appHost = WithModules(Services, "billing")["src/AppHost/AppHost.cs"];

        appHost.ShouldContain("""builder.AddBuiltProject("host", "../Edge/Edge.csproj")""");
        appHost.ShouldContain("builder.AddBuiltProject(module, project)");
    }

    [Fact]
    public void The_integration_tests_check_that_every_project_starts_from_the_AppHosts_build()
    {
        var tests = WithModules(Services)["tests/Integration.Tests/HostTests.cs"];

        tests.ShouldContain("public async Task Every_project_starts_from_the_AppHosts_build()");
        tests.ShouldContain("GetProjectMetadata().SuppressBuild");
    }

    [Fact]
    public void A_services_project_passes_the_rule_and_fails_it_naming_a_project_added_by_path()
    {
        using var project = new GeneratedProject(Services);
        project.AddModule("billing");
        project.Write("src/AppHost/Wiring/Extra.cs", """
            namespace Aspire.Hosting;

            public static class Extra
            {
                public static void AddExtra(this IDistributedApplicationBuilder builder) =>
                    builder.AddProject("extra", "../Extra/Extra.csproj");
            }

            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/AppHost/Wiring/Extra.cs:6: adds a project by path with AddProject, so Aspire rebuilds it at every start; add it with AddBuiltProject, which runs it from the AppHost's build.");
        output.ShouldNotContain("src/AppHost/AppHost.cs:");
        output.ShouldNotContain("src/AppHost/Wiring/BuiltProjects.cs:");
    }

    [Theory]
    [InlineData(Monolith)]
    [InlineData(Services)]
    public void Every_job_of_the_generated_CI_has_a_timeout(string profile)
    {
        var ci = new YamlStream();
        ci.Load(new StringReader(WithModules(profile)[".github/workflows/ci.yml"]));
        var jobs = (YamlMappingNode)((YamlMappingNode)ci.Documents[0].RootNode).Children[new YamlScalarNode("jobs")];

        var timeouts = jobs.Children.ToDictionary(
            job => ((YamlScalarNode)job.Key).Value!,
            job => ((YamlMappingNode)job.Value).Children.TryGetValue(new YamlScalarNode("timeout-minutes"), out var minutes) ? ((YamlScalarNode)minutes).Value : null);

        timeouts.ShouldBe(new Dictionary<string, string?> { ["backend"] = "25", ["frontend"] = "10", ["e2e"] = "30" }, ignoreOrder: true);
    }

    [Fact]
    public void The_integration_fixture_reports_how_long_the_application_took_to_start_and_says_how_its_limit_was_chosen()
    {
        var fixture = WithModules(Services)["tests/Integration.Tests/AspireFixture.cs"];

        fixture.ShouldContain("profile-saas-services-vue");
        fixture.ShouldContain("Stopwatch.StartNew()");
        fixture.ShouldContain("The application started in ");
        fixture.ShouldNotContain("TimeSpan.FromMinutes(5)");
    }

    private static Dictionary<string, string> WithModules(string profile, params string[] modules)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var record = FabricFile.Read(new StringReader(files[FabricFile.FileName]), FabricFile.FileName);
            var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, module, []);
            addition.Errors.ShouldBeEmpty();
            foreach (var file in addition.Created)
            {
                files.Add(file.Path, file.Content);
            }

            files[FabricFile.FileName] = addition.FabricYaml!;
        }

        return files;
    }

    /// <summary>A call of the AddProject overloads that take a project path rather than a <c>Projects</c> type.</summary>
    [GeneratedRegex(@"\.AddProject\(")]
    private static partial Regex ByPath();
}

using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// A generated CI job that starts an AppHost names Docker as Aspire's container runtime (#830). Unnamed, Aspire 13.6.5's
/// orchestrator also runs <c>podman container ls</c>, and GitHub's runners carry podman: that call hung past the job and
/// failed the AppHost's dependency check. A job starts an AppHost when it runs <c>.github/scripts/e2e.sh</c>, or runs
/// <c>.github/scripts/test.sh</c> in a project whose tests reference Aspire's testing host. A job that starts none names
/// no runtime.
/// </summary>
public sealed class ContainerRuntimeTests
{
    private const string Variable = "ASPIRE_CONTAINER_RUNTIME";
    private const string AspireTestingHost = "<PackageReference Include=\"Aspire.Hosting.Testing\" />";

    public static TheoryData<string> Profiles => [.. Repository.Catalog.Profiles.Keys];

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_job_of_a_profile_that_starts_an_AppHost_names_Docker(string profile)
    {
        var starting = Jobs(profile).Where(job => job.StartsAnAppHost).ToList();

        starting.ShouldNotBeEmpty();
        starting.ShouldAllBe(job => job.Runtime == "docker");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void No_job_of_a_profile_that_starts_no_AppHost_names_a_runtime(string profile) =>
        Jobs(profile).Where(job => !job.StartsAnAppHost).ShouldAllBe(job => job.Runtime == null);

    [Fact]
    public void With_testcontainers_the_backend_job_starts_no_AppHost_and_names_no_runtime()
    {
        var jobs = Jobs("saas-standard-react", "backend.test.integration=testcontainers").ToDictionary(job => job.Name);

        jobs["backend"].StartsAnAppHost.ShouldBeFalse();
        jobs["backend"].Runtime.ShouldBeNull();
        jobs["e2e"].Runtime.ShouldBe("docker");
    }

    private static List<Job> Jobs(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var testsStartTheAppHost = generated.Files
            .Any(file => file.Path.StartsWith("tests/", StringComparison.Ordinal) && file.Path.EndsWith(".csproj", StringComparison.Ordinal) && file.Content.Contains(AspireTestingHost, StringComparison.Ordinal));
        var stream = new YamlStream();
        stream.Load(new StringReader(generated.Files.Single(file => file.Path == ".github/workflows/ci.yml").Content));
        var jobs = (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode)["jobs"];

        return [.. jobs.Children.Select(job =>
        {
            var runs = ((YamlSequenceNode)((YamlMappingNode)job.Value)["steps"]).Children
                .Select(step => Scalar(step, "run") ?? "")
                .ToList();
            var startsAnAppHost = runs.Any(run => run.Contains(".github/scripts/e2e.sh", StringComparison.Ordinal))
                || (testsStartTheAppHost && runs.Any(run => run.Contains(".github/scripts/test.sh", StringComparison.Ordinal)));
            return new Job(((YamlScalarNode)job.Key).Value!, startsAnAppHost, Scalar(Value(job.Value, "env"), Variable));
        })];
    }

    private static YamlNode? Value(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Scalar(YamlNode? node, string key) => (Value(node, key) as YamlScalarNode)?.Value;

    private sealed record Job(string Name, bool StartsAnAppHost, string? Runtime);
}

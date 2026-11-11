using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Fabric.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// The CLI ships as a .NET tool with the catalog and the packs inside it, versioned with the SemVer <c>fabric --version</c>
/// reports (owner decision E5, #381). build/tool-check.sh packs it, installs it from a folder feed into an empty
/// directory, generates a profile with it and builds the result; the release workflow runs the same script on a
/// <c>v*</c> tag before it publishes to the organisation's GitHub Packages feed. A pull request never publishes.
/// </summary>
public sealed class ToolPackageTests
{
    private const string Feed = "https://nuget.pkg.github.com/smartsolutionslab/index.json";

    private const string RepositoryUrl = "https://github.com/smartsolutionslab/software-fabric";

    private static readonly string Version =
        typeof(Generator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    [Fact]
    public void The_packed_tool_installs_from_a_folder_feed_reports_its_package_version_and_generates_a_profile_that_builds()
    {
        var feed = Directory.CreateTempSubdirectory("fabric-feed-");
        var cache = Directory.CreateTempSubdirectory("fabric-packages-");
        try
        {
            // An empty package cache, as on a clean runner: the generated project restores everything from its sources.
            var (exitCode, output) = Shell.Run(
                Repository.Root,
                new Dictionary<string, string> { ["NUGET_PACKAGES"] = cache.FullName },
                "bash",
                "build/tool-check.sh",
                "lean-crud",
                feed.FullName);

            exitCode.ShouldBe(0, output);
            output.ShouldContain(
                $"fabric {Version}: packed, installed from a folder feed, generated lean-crud as the repository's CLI does, and built it.",
                Case.Sensitive,
                output);
            var package = Path.Combine(feed.FullName, $"fabric.{Version}.nupkg");
            File.Exists(package).ShouldBeTrue(output);
            using var archive = ZipFile.OpenRead(package);
            var entries = archive.Entries.Select(entry => entry.FullName).ToList();
            entries.ShouldContain("tools/net11.0/any/catalog/catalog.yaml");
            entries.ShouldContain("tools/net11.0/any/packs/base/core/pack.yaml");
            var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using var stream = nuspec.Open();
            var repository = XDocument.Load(stream).Descendants().Single(element => element.Name.LocalName == "repository");
            ((string?)repository.Attribute("url")).ShouldBe(RepositoryUrl, "GitHub Packages links a package to the repository its metadata names.");
        }
        finally
        {
            feed.Delete(recursive: true);
            cache.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_release_whose_tag_does_not_name_the_version_fails_before_anything_is_packed()
    {
        var release = Version.Split('-')[0];
        var feed = Directory.CreateTempSubdirectory("fabric-feed-");
        try
        {
            var (exitCode, output) = Shell.Run(Repository.Root, "bash", "build/tool-check.sh", "lean-crud", feed.FullName, "--release", "v0.0.0");

            exitCode.ShouldNotBe(0, output);
            output.ShouldContain($"the tag v0.0.0 does not name the version this commit releases, {release}; tag it v{release}.", Case.Sensitive, output);
            feed.EnumerateFiles().ShouldBeEmpty(output);
        }
        finally
        {
            feed.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_release_workflow_runs_only_on_a_v_tag_and_publishes_the_checked_package_to_the_organisation_s_feed()
    {
        var workflow = Workflow("release.yml");

        var triggers = (YamlMappingNode)workflow["on"];
        triggers.Children.Keys.Select(key => key.ToString()).ShouldBe(["push"], "a release is only ever started by a tag.");
        var push = (YamlMappingNode)triggers["push"];
        push.Children.Keys.Select(key => key.ToString()).ShouldBe(["tags"], "a push to a branch publishes nothing.");
        ((YamlSequenceNode)push["tags"]).Children.Select(tag => tag.ToString()).ShouldBe(["v*"]);

        var job = (YamlMappingNode)((YamlMappingNode)workflow["jobs"])["release"];
        var permissions = (YamlMappingNode)job["permissions"];
        Scalar(permissions, "packages").ShouldBe("write");
        Scalar(permissions, "contents").ShouldBe("read");
        var runs = ((YamlSequenceNode)job["steps"]).Children.Cast<YamlMappingNode>()
            .Select(step => Scalar(step, "run"))
            .OfType<string>()
            .Select(run => run.Trim())
            .ToList();

        var build = runs.FindIndex(run => run.StartsWith("dotnet build Fabric.slnx -c Release -p:VersionSuffix=", StringComparison.Ordinal));
        var tests = runs.FindIndex(run => run.Contains("dotnet test", StringComparison.Ordinal));
        var check = runs.FindIndex(run => run.StartsWith("build/tool-check.sh ", StringComparison.Ordinal) && run.EndsWith("--release \"$GITHUB_REF_NAME\"", StringComparison.Ordinal));
        var publish = runs.FindIndex(run => run.StartsWith("dotnet nuget push ", StringComparison.Ordinal));
        build.ShouldBeGreaterThanOrEqualTo(0, "the release builds with the version suffix cleared (owner decision on #381).");
        tests.ShouldBeGreaterThan(build, "the release tests what it built.");
        check.ShouldBeGreaterThan(tests, "the package is installed and generates a project that builds before it is published.");
        publish.ShouldBeGreaterThan(check);
        runs[publish].ShouldContain($"--source {Feed}");
        runs.Count(run => run.Contains("nuget push", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public void No_workflow_but_the_release_workflow_publishes_a_package()
    {
        var publishing = Directory.EnumerateFiles(Path.Combine(Repository.Root, ".github", "workflows"))
            .Where(path => File.ReadAllText(path).Contains("nuget push", StringComparison.Ordinal))
            .Select(Path.GetFileName);

        publishing.ShouldBe(["release.yml"]);
    }

    private static YamlMappingNode Workflow(string name)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", name))));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;
}

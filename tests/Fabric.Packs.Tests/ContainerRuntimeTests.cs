using YamlDotNet.RepresentationModel;

namespace Fabric.Packs.Tests;

/// <summary>
/// Every workflow job that sets up the Aspire CLI bundle, to start AppHosts, names Docker as their container runtime
/// (#827). Without a runtime named, Aspire's orchestrator asks every runtime it knows whether it runs, podman included,
/// which GitHub's runners carry. A <c>podman container ls</c> that hung there, and outlived the job, failed the AppHost's
/// dependency check in three test classes ("Application orchestrator dependency check returned an error: The operation
/// has timed out"). Each case starts a minimal AppHost with its job's environment and a podman that only records that it
/// was asked.
/// </summary>
public sealed class ContainerRuntimeTests : IClassFixture<ContainerRuntimeTests.MinimalAppHost>
{
    private readonly MinimalAppHost appHost;

    public ContainerRuntimeTests(MinimalAppHost appHost)
    {
        this.appHost = appHost;
    }

    public static TheoryData<string, string> Jobs
    {
        get
        {
            var jobs = new TheoryData<string, string>();
            foreach (var path in Directory.EnumerateFiles(Path.Combine(Repository.Root, ".github", "workflows"), "*.yml").Order(StringComparer.Ordinal))
            {
                var workflow = Workflow(path);
                foreach (var (name, job) in (YamlMappingNode)workflow["jobs"])
                {
                    if (SetsUpAspire((YamlMappingNode)job))
                    {
                        jobs.Add(Path.GetFileName(path), ((YamlScalarNode)name).Value!);
                    }
                }
            }

            return jobs;
        }
    }

    [Fact]
    public void Some_workflow_job_sets_up_the_Aspire_CLI()
    {
        Jobs.Count.ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(Jobs))]
    public void An_AppHost_started_in_the_jobs_environment_never_asks_podman(string workflow, string job)
    {
        var workflowNode = Workflow(Path.Combine(Repository.Root, ".github", "workflows", workflow));
        var jobNode = (YamlMappingNode)((YamlMappingNode)workflowNode["jobs"])[job];
        // Only the job's own setting counts, not one this test process inherited: an empty value is no runtime named.
        var environment = new Dictionary<string, string> { ["ASPIRE_CONTAINER_RUNTIME"] = string.Empty, ["DOTNET_ASPIRE_CONTAINER_RUNTIME"] = string.Empty };
        foreach (var (name, value) in Environment(workflowNode).Concat(Environment(jobNode)))
        {
            environment[name] = value;
        }

        var (exitCode, output, podman) = appHost.Start(environment);

        exitCode.ShouldBe(0, output);
        podman.ShouldBeEmpty($"{workflow}'s {job} job lets Aspire ask podman whether it runs; name Docker with ASPIRE_CONTAINER_RUNTIME.");
    }

    private static YamlMappingNode Workflow(string path)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static bool SetsUpAspire(YamlMappingNode job) =>
        job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps)
        && ((YamlSequenceNode)steps).Children.Cast<YamlMappingNode>().Any(step =>
            step.Children.TryGetValue(new YamlScalarNode("run"), out var run)
            && ((YamlScalarNode)run).Value!.Contains("aspire.cli@", StringComparison.Ordinal));

    /// <summary>The literal variables of a workflow's or job's <c>env</c>; an expression is the runner's to fill.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Environment(YamlMappingNode node) =>
        node.Children.TryGetValue(new YamlScalarNode("env"), out var env)
            ? ((YamlMappingNode)env).Children
                .Select(entry => KeyValuePair.Create(((YamlScalarNode)entry.Key).Value!, ((YamlScalarNode)entry.Value).Value!))
                .Where(entry => !entry.Value.Contains("${{", StringComparison.Ordinal))
            : [];

    /// <summary>An AppHost with no resources that starts and stops, built once, and a podman that records its arguments.</summary>
    public sealed class MinimalAppHost : IDisposable
    {
        private const string Program = """
            var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = args, DisableDashboard = true });
            await using var app = builder.Build();
            await app.StartAsync();
            await app.StopAsync();
            """;

        private readonly string root = Directory.CreateTempSubdirectory("fabric-apphost-").FullName;

        public MinimalAppHost()
        {
            File.WriteAllText(Path.Combine(root, "AppHost.csproj"), $"""
                <Project Sdk="Aspire.AppHost.Sdk/13.6.5">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>{GeneratedProject.TargetFramework}</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), Program);
            var (exitCode, output) = Shell.Run(root, "dotnet", "build", "-c", "Release");
            exitCode.ShouldBe(0, output);

            var podman = Path.Combine(root, "podman-bin", "podman");
            Directory.CreateDirectory(Path.GetDirectoryName(podman)!);
            File.WriteAllText(podman, $"#!/bin/sh\necho \"$*\" >> \"{Calls}\"\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(podman, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        private string Calls => Path.Combine(root, "podman-calls");

        /// <summary>Starts and stops the AppHost with the given environment and the recording podman first on the PATH.</summary>
        public (int ExitCode, string Output, string Podman) Start(IReadOnlyDictionary<string, string> environment)
        {
            File.Delete(Calls);
            var path = $"{Path.Combine(root, "podman-bin")}:{System.Environment.GetEnvironmentVariable("PATH")}";
            var (exitCode, output) = Shell.Run(
                root,
                new Dictionary<string, string>(environment) { ["PATH"] = path },
                "dotnet",
                Path.Combine("bin", "Release", GeneratedProject.TargetFramework, "AppHost.dll"));
            return (exitCode, output, File.Exists(Calls) ? File.ReadAllText(Calls) : string.Empty);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}

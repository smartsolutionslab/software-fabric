using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>A saas-standard-react project generated from the real packs (stand-ins for the rest) into a temp directory.</summary>
internal sealed class GeneratedProject : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("fabric-saas-").FullName;
    private readonly StandIns standIns = new();
    private readonly IReadOnlyDictionary<string, Core.Packs.PackManifest> packs;

    public GeneratedProject(string profile, params string[] overrides)
        : this(profile, [], overrides)
    {
    }

    /// <param name="replacements">Packs that take the place of the real pack or stand-in for their option.</param>
    public GeneratedProject(string profile, IReadOnlyList<Core.Packs.PackManifest> replacements, params string[] overrides)
    {
        var completed = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, profile, overrides).ToDictionary();
        foreach (var replacement in replacements)
        {
            completed[replacement.Option] = replacement;
        }

        packs = completed;
        ProjectDirectory = Path.Combine(root, "shop");

        var generated = Generator.New(Repository.Catalog, packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        ProjectWriter.Write(ProjectDirectory, generated.Files).ShouldBeNull();
    }

    public string ProjectDirectory { get; }

    public void AddModule(string name)
    {
        var record = FabricFile.Read(new StringReader(File.ReadAllText(Path.Combine(ProjectDirectory, FabricFile.FileName))), FabricFile.FileName);
        var addition = Generator.AddModule(Repository.Catalog, packs, record, name, []);
        addition.Errors.ShouldBeEmpty();
        ProjectWriter.Add(ProjectDirectory, addition.Created, addition.FabricYaml!).ShouldBeNull();
    }

    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(ProjectDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Build()
    {
        var (exitCode, output) = TryBuild();
        exitCode.ShouldBe(0, output);
    }

    public (int ExitCode, string Output) DotNetCommand(IReadOnlyDictionary<string, string> environment, params string[] arguments) =>
        DotNet.Run(ProjectDirectory, environment, arguments);

    public (int ExitCode, string Output) TryBuild() =>
        DotNet.Run(ProjectDirectory, new Dictionary<string, string>(), "build", "build.proj", "-c", "Release");

    /// <summary>Runs one test project of the generated solution, already built in Release.</summary>
    public (int ExitCode, string Output) Test(string project, IReadOnlyDictionary<string, string>? environment = null) =>
        DotNet.Run(ProjectDirectory, environment ?? new Dictionary<string, string>(), "test", "--project", project, "-c", "Release", "--no-build", "--no-ansi");

    /// <summary>Starts the built host on a free port, outside Aspire, with the given configuration.</summary>
    public RunningHost StartHost(IReadOnlyDictionary<string, string> environment)
    {
        var port = FreePort();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(ProjectDirectory, "src", "Host", "bin", "Release", "net10.0"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("Host.dll");
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        return new RunningHost(Process.Start(start)!, new Uri($"http://127.0.0.1:{port}"));
    }

    public void Dispose()
    {
        standIns.Dispose();

        // Processes a stopped AppHost started (the Vite server, its installer) can still be writing for a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

internal sealed class RunningHost(Process process, Uri address) : IDisposable
{
    private readonly HttpClient client = new() { BaseAddress = address };

    public async Task<HttpResponseMessage> PostAsync(string path)
    {
        await GetAsync("/alive");
        return await client.PostAsync(new Uri(path, UriKind.Relative), null);
    }

    /// <summary>Waits until /alive answers, or fails with the host's output.</summary>
    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                if ((await client.GetAsync(new Uri("/alive", UriKind.Relative))).IsSuccessStatusCode)
                {
                    return await client.GetAsync(new Uri(path, UriKind.Relative));
                }
            }
            catch (HttpRequestException)
            {
                if (process.HasExited)
                {
                    break;
                }
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException($"The host did not start:\n{await process.StandardOutput.ReadToEndAsync()}{await process.StandardError.ReadToEndAsync()}");
    }

    public void Dispose()
    {
        client.Dispose();
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.Dispose();
    }
}

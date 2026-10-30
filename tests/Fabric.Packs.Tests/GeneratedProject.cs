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

    public GeneratedProject(string profile)
    {
        packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, profile);
        ProjectDirectory = Path.Combine(root, "shop");

        var generated = Generator.New(Repository.Catalog, packs, "shop", profile, []);
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
        var (exitCode, output) = DotNet.Run(ProjectDirectory, "build", "build.proj", "-c", "Release");
        exitCode.ShouldBe(0, output);
    }

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
        Directory.Delete(root, recursive: true);
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

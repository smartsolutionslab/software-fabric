using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>A saas-standard-react project generated from the real packs (stand-ins for the rest) into a temp directory.</summary>
internal sealed partial class GeneratedProject : IDisposable
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

    /// <summary>The target framework a generated project builds for, as the core pack's Directory.Build.props sets it.</summary>
    public static string TargetFramework { get; } = TargetFrameworkElement().Match(
        File.ReadAllText(Path.Combine(Repository.Root, "packs", "base", "core", "templates", "Directory.Build.props.sbn"))).Groups[1].Value;

    public string ProjectDirectory { get; }

    public void AddModule(string name)
    {
        var record = FabricFile.Read(new StringReader(File.ReadAllText(Path.Combine(ProjectDirectory, FabricFile.FileName))), FabricFile.FileName);
        var addition = Generator.AddModule(Repository.Catalog, packs, record, name, []);
        addition.Errors.ShouldBeEmpty();
        ProjectWriter.Add(ProjectDirectory, addition.Created, addition.FabricYaml!, addition.OpenApi).ShouldBeNull();
    }

    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(ProjectDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Writes each top-level type of a C# source with a file-scoped namespace into its own file in the directory, named
    /// after the type, as the generated project requires (SA1402, SA1649). Every file gets the source's usings and
    /// namespace; a type's doc comments, attributes and the pragmas right above it go with it.
    /// </summary>
    public void WriteTypes(string relativeDirectory, string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var namespaceLine = Array.FindIndex(lines, line => line.StartsWith("namespace ", StringComparison.Ordinal));
        namespaceLine.ShouldBeGreaterThanOrEqualTo(0, "WriteTypes needs a file-scoped namespace.");
        var header = string.Join('\n', lines[..(namespaceLine + 1)]);

        List<(string Name, int Start)> types = [];
        for (var index = namespaceLine + 1; index < lines.Length; index++)
        {
            var declaration = TopLevelType().Match(lines[index]);
            if (!declaration.Success)
            {
                continue;
            }

            var start = index;
            while (start > namespaceLine + 1 && (lines[start - 1].StartsWith("///", StringComparison.Ordinal) || lines[start - 1].StartsWith('[') || lines[start - 1].StartsWith("#pragma", StringComparison.Ordinal)))
            {
                start--;
            }

            types.Add((declaration.Groups["name"].Value, start));
        }

        types.ShouldNotBeEmpty("WriteTypes found no top-level type.");
        for (var position = 0; position < types.Count; position++)
        {
            var end = position + 1 < types.Count ? types[position + 1].Start : lines.Length;
            var body = string.Join('\n', lines[types[position].Start..end]).TrimEnd();
            Write(Path.Combine(relativeDirectory, $"{types[position].Name}.cs"), $"{header}\n\n{body}\n");
        }
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
            WorkingDirectory = Path.Combine(ProjectDirectory, "src", "Host", "bin", "Release", TargetFramework),
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

    [GeneratedRegex(@"^(?:(?:public|internal|static|sealed|abstract|partial|readonly|ref)\s+)*(?:class|record\s+struct|record|struct|interface|enum|delegate\s+\S+)\s+(?<name>\w+)")]
    private static partial Regex TopLevelType();

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [GeneratedRegex("<TargetFramework>([^<]+)</TargetFramework>")]
    private static partial Regex TargetFrameworkElement();
}

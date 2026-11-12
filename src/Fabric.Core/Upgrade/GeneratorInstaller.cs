namespace Fabric.Core.Upgrade;

/// <summary>
/// Installs a released fabric from the organisation's feed into a scratch tool path (ADR-0016 D2 a), and from nowhere
/// else: a nuget.config beside the install clears every other source, so a package of the same name on another feed
/// is never run. The feed's credentials are the ones the person stored for it under the name the README gives it.
/// </summary>
internal static class GeneratorInstaller
{
    public const string Feed = "https://nuget.pkg.github.com/smartsolutionslab/index.json";

    private const string SourceName = "smartsolutionslab";

    public static (string? Fabric, IReadOnlyList<string> Errors) Install(string version, string scratch)
    {
        var directory = Path.Combine(scratch, "install");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="{SourceName}" value="{Feed}" />
              </packageSources>
            </configuration>

            """);

        var tools = Path.Combine(directory, "tools");
        var installed = Processes.Run("dotnet", ["tool", "install", "fabric", "--version", version, "--tool-path", tools], directory);
        if (installed.ExitCode != 0)
        {
            return (null, [
                $"could not install fabric {version} from {Feed}; add that feed as {SourceName} with a token that may read packages " +
                $"(README), or pass --from <path to fabric {version}>.",
                installed.Printed,
            ]);
        }

        return (Path.Combine(tools, OperatingSystem.IsWindows() ? "fabric.exe" : "fabric"), []);
    }
}

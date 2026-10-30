using System.Diagnostics;

namespace Fabric.Packs.Tests;

internal static class DotNet
{
    /// <returns>The exit code and everything the command wrote.</returns>
    public static (int ExitCode, string Output) Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        var standardError = process.StandardError.ReadToEndAsync();
        var standardOutput = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput + standardError.Result);
    }
}

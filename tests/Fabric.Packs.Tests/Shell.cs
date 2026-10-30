using System.Diagnostics;

namespace Fabric.Packs.Tests;

internal static class Shell
{
    public static (int ExitCode, string Output) Run(string workingDirectory, string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }
}

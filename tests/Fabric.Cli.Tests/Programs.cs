using System.Diagnostics;

namespace Fabric.Cli.Tests;

internal static class Programs
{
    public static (int ExitCode, string Output, string Error) Run(string fileName, string directory, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }
}

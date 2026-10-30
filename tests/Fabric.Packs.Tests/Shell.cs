using System.Diagnostics;

namespace Fabric.Packs.Tests;

internal static class Shell
{
    public static (int ExitCode, string Output) Run(string workingDirectory, string command, params string[] arguments) =>
        Run(workingDirectory, new Dictionary<string, string>(), command, arguments);

    public static (int ExitCode, string Output) Run(string workingDirectory, IReadOnlyDictionary<string, string> environment, string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }
}

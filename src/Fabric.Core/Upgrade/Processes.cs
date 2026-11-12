using System.ComponentModel;
using System.Diagnostics;

namespace Fabric.Core.Upgrade;

internal static class Processes
{
    /// <summary>A program that cannot be started reports that as its error, with exit code 127, as a shell would.</summary>
    public static ProcessResult Run(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        string? outputFile = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception($"{fileName} did not start.");
        }
        catch (Win32Exception exception)
        {
            return new ProcessResult(127, string.Empty, $"cannot run {fileName}: {exception.Message}");
        }

        using (process)
        {
            var error = process.StandardError.ReadToEndAsync();
            var output = string.Empty;
            if (outputFile is null)
            {
                output = process.StandardOutput.ReadToEnd();
            }
            else
            {
                using var file = File.Create(outputFile);
                process.StandardOutput.BaseStream.CopyTo(file);
            }

            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output, error.Result);
        }
    }
}

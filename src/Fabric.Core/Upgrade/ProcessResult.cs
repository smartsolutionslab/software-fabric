namespace Fabric.Core.Upgrade;

/// <summary>What a program that ran printed and how it exited.</summary>
internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    /// <summary>Everything it printed, on one line, for an error message.</summary>
    public string Printed => string.Join(' ', (Error + "\n" + Output).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}

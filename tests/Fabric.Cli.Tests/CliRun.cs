namespace Fabric.Cli.Tests;

internal sealed record CliRun(int ExitCode, string Output, string Error)
{
    public static CliRun Of(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = FabricCli.Run(args, output, error);
        return new CliRun(exitCode, output.ToString(), error.ToString());
    }
}

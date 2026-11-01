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

internal static class Paths
{
    public static string Repository { get; } = Locate();

    public static string FixtureCatalog { get; } = Path.Combine(Repository, "tests", "fixtures", "minimal", "catalog.yaml");

    public static string FixturePacks { get; } = Path.Combine(Repository, "tests", "fixtures", "minimal", "packs");

    public static string RepositoryCatalog { get; } = Path.Combine(Repository, "catalog", "catalog.yaml");

    public static string RepositoryPacks { get; } = Path.Combine(Repository, "packs");

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fabric.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Fabric.slnx not found above the test output directory.");
    }
}

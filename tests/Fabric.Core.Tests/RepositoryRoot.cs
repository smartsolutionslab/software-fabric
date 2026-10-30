namespace Fabric.Core.Tests;

internal static class RepositoryRoot
{
    public static string Path { get; } = Locate();

    public static string File(params string[] segments) =>
        System.IO.Path.Combine([Path, .. segments]);

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Fabric.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Fabric.slnx not found above the test output directory.");
    }
}

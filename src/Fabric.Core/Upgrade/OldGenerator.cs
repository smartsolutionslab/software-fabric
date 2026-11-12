namespace Fabric.Core.Upgrade;

/// <summary>
/// The fabric that wrote the project, run as its own process: an installed tool, a <c>fabric.dll</c> (run with
/// <c>dotnet</c>), or any executable that behaves as one. It is driven only through <c>--version</c>, <c>new</c> and
/// <c>add-module</c>, which every released fabric has.
/// </summary>
internal sealed class OldGenerator(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public ProcessResult Run(string workingDirectory, params string[] arguments) =>
        Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? Processes.Run("dotnet", [Path, .. arguments], workingDirectory)
            : Processes.Run(Path, arguments, workingDirectory);
}

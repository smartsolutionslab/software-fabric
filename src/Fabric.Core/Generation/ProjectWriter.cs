using System.Text;
using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

public static class ProjectWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes into a new or empty directory. Checks every path before writing anything.</summary>
    /// <returns>An error, or <c>null</c> when every file was written.</returns>
    public static string? Write(string directory, IReadOnlyList<RenderedFile> files)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(files);

        var root = Path.GetFullPath(directory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            return $"{directory} is not empty; fabric new only writes into an empty or new directory.";
        }

        List<(string FullPath, string Content)> targets = [];
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, file.Path));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return $"{file.Path} points outside the project directory.";
            }

            targets.Add((fullPath, file.Content));
        }

        foreach (var (fullPath, content) in targets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, Utf8WithoutBom);
        }

        return null;
    }

    /// <summary>Adds new files to an existing project and replaces its <c>fabric.yaml</c>. Never overwrites anything else.</summary>
    /// <returns>An error, or <c>null</c> when everything was written.</returns>
    public static string? Add(string directory, IReadOnlyList<RenderedFile> files, string fabricYaml)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(fabricYaml);

        var root = Path.GetFullPath(directory);
        List<(string FullPath, string Content)> targets = [];
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, file.Path));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return $"{file.Path} points outside the project directory.";
            }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return $"{file.Path} already exists; add-module does not overwrite files.";
            }

            targets.Add((fullPath, file.Content));
        }

        targets.Add((Path.Combine(root, FabricFile.FileName), fabricYaml));
        foreach (var (fullPath, content) in targets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, Utf8WithoutBom);
        }

        return null;
    }
}

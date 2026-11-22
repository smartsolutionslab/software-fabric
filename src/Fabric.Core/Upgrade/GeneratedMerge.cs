using Fabric.Core.Catalog;
using Fabric.Core.Generation;
using Fabric.Core.Rendering;

namespace Fabric.Core.Upgrade;

/// <summary>
/// What <c>fabric change</c> and <c>add-module</c> share when they merge (ADR-0016 C3): the running fabric writes base and
/// theirs, so the project must be one this fabric and catalog wrote, and theirs is merged into it with D8's merge.
/// </summary>
internal static class GeneratedMerge
{
    /// <summary>
    /// Base is what the running fabric writes for the recorded choices, so the project must be one this fabric and catalog
    /// wrote (ADR-0016 C3); otherwise every difference between the generators would be taken as the project's own edit.
    /// </summary>
    /// <param name="command">The command that merges, as the refusal names it: <c>fabric change</c>.</param>
    /// <param name="merged">What it merges, as the refusal names it: <c>the change</c>.</param>
    public static string? NotThisFabric(FabricRecord record, CatalogModel catalog, string command, string merged)
    {
        if (record.Generator != GeneratorVersion.Current)
        {
            var writer = record.Generator is { } version ? $"was written by fabric {version}" : "records no generator version";
            return $"{FabricFile.FileName} {writer}, not this fabric {GeneratorVersion.Current}; run fabric upgrade first, so {merged} " +
                $"is the only difference {command} merges (ADR-0016 C3).";
        }

        return record.Catalog == catalog.Hash
            ? null
            : $"{FabricFile.FileName} records catalog {record.Catalog ?? "none"}, but this fabric's catalog is {catalog.Hash}; " +
              $"{command} merges against what this catalog writes, so it refuses a project another catalog generated (ADR-0016 C3).";
    }

    /// <summary>
    /// Writes base and theirs as tree objects and merges theirs into the project with base as the merge base (D8), or
    /// says why a tree could not be written.
    /// </summary>
    public static (UpgradeOutcome? Outcome, string? Error) Merge(
        GitRepository repository,
        string name,
        IReadOnlyList<RenderedFile> baseFiles,
        IReadOnlyList<RenderedFile> theirsFiles,
        (string Project, string Base, string Theirs) labels)
    {
        var scratch = Directory.CreateTempSubdirectory("fabric-merge-").FullName;
        try
        {
            var (baseTree, baseError) = WriteTree(repository, baseFiles, Path.Combine(scratch, "base"), name);
            var (theirsTree, theirsError) = WriteTree(repository, theirsFiles, Path.Combine(scratch, "theirs"), name);
            if (baseTree is null || theirsTree is null)
            {
                return (null, baseError ?? theirsError);
            }

            return (repository.Merge(baseTree, theirsTree, labels, Path.Combine(scratch, "merge")), null);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static (string? Tree, string? Error) WriteTree(GitRepository repository, IReadOnlyList<RenderedFile> files, string scratch, string name)
    {
        var directory = Path.Combine(scratch, name);
        return ProjectWriter.Write(directory, files) is { } error
            ? (null, error)
            : (repository.WriteTree(directory, scratch + ".index"), null);
    }
}

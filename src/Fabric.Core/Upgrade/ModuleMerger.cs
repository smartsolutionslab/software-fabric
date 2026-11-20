using Fabric.Core.Catalog;
using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Core.Upgrade;

/// <summary>
/// <c>add-module</c> for a module that selects packs new to the project, another style or event store (ADR-0005, amended
/// 2026-11-20): this fabric writes the project from the <c>fabric.yaml</c> it records (base) and with the module (theirs),
/// and three-way merges theirs into the project with base as the merge base, as <c>fabric change</c> does (ADR-0016 C3).
/// Every check runs before the project is touched.
/// </summary>
public static class ModuleMerger
{
    /// <param name="addition">What <see cref="Generator.AddModule"/> gave for the module: the whole project with it.</param>
    public static UpgradeResult Merge(
        string projectDirectory,
        FabricRecord record,
        string module,
        ModuleAddition addition,
        CatalogModel catalog,
        IReadOnlyDictionary<string, PackManifest> packs)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(addition);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(addition.Project);

        var newPacks = string.Join(", ", addition.NewPacks);
        var (repository, gitError) = GitRepository.Open(projectDirectory);
        if (repository is null)
        {
            var (verb, writer) = addition.NewPacks.Count == 1 ? ("is", "its pack writes") : ("are", "their packs write");
            return Failure($"{newPacks} {verb} new to the project, so add-module merges what {writer} into it, as fabric change " +
                $"merges a choice (ADR-0005): {gitError}");
        }

        if (GeneratedMerge.NotThisFabric(record, catalog, "add-module", "the module") is { } refusal)
        {
            return Failure(refusal);
        }

        var baseGeneration = Generator.Regenerate(catalog, packs, record);
        if (baseGeneration.Errors.Count > 0)
        {
            return new UpgradeResult(null, [$"fabric {GeneratorVersion.Current} cannot regenerate {FabricFile.FileName}:", .. baseGeneration.Errors]);
        }

        var theirs = addition.Project;
        var (outcome, mergeError) = GeneratedMerge.Merge(
            repository,
            record.Name,
            baseGeneration.Files,
            theirs,
            ("project", $"without module {module}", $"add-module {module}"));
        if (outcome is null)
        {
            return Failure(mergeError!);
        }

        var hasOpenApiDocument = theirs.Any(file => file.Path == UpgradeSummary.OpenApiDocument);
        var hasBaseApi = theirs.Any(file => file.Path == UpgradeSummary.BaseApi);
        return new UpgradeResult(UpgradeSummary.WriteModule(record.Name, module, addition.NewPacks, outcome, hasOpenApiDocument, hasBaseApi), [])
        {
            Experimental = addition.Experimental,
        };
    }

    private static UpgradeResult Failure(string error) => new(null, [error]);
}

using Fabric.Core.Catalog;
using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Core.Upgrade;

/// <summary>
/// <c>fabric change</c> (ADR-0016 C1 to C3): this fabric writes the project from the choices <c>fabric.yaml</c> records
/// (base) and from the same choices with the changed ones (theirs), and three-way merges theirs into the project with
/// base as the merge base, as <c>fabric upgrade</c> does (D8). Only dimensions the catalog marks changeable may be set,
/// and only in a project this fabric and catalog wrote. Every check runs before the project is touched.
/// </summary>
public static class ChoiceChanger
{
    public static UpgradeResult Change(
        string projectDirectory,
        IReadOnlyList<string> assignments,
        CatalogModel catalog,
        IReadOnlyDictionary<string, PackManifest> packs)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);

        var fabricYamlPath = Path.Combine(projectDirectory, FabricFile.FileName);
        if (!File.Exists(fabricYamlPath))
        {
            return Failure([$"no {FabricFile.FileName} in {projectDirectory}; run change in a generated project or pass --project."]);
        }

        FabricRecord record;
        using (var reader = File.OpenText(fabricYamlPath))
        {
            record = FabricFile.Read(reader, fabricYamlPath);
        }

        var (changed, refusals) = Parse(assignments, catalog);
        if (refusals.Count > 0)
        {
            return Failure(refusals);
        }

        var (repository, gitError) = GitRepository.Open(projectDirectory);
        if (repository is null)
        {
            return Failure([gitError!]);
        }

        if (GeneratedMerge.NotThisFabric(record, catalog, "fabric change", "the change") is { } refusal)
        {
            return Failure([refusal]);
        }

        List<ChoiceChange> changes = [.. changed.Select(change => new ChoiceChange(
            change.Key,
            record.Choices.Project.TryGetValue(change.Key, out var recorded) ? string.Join(',', recorded) : "nothing",
            string.Join(',', change.Value)))];
        if (changes.FirstOrDefault(change => change.From == change.To) is { } unchanged)
        {
            return Failure([$"{FabricFile.FileName} already records {unchanged.Dimension}={unchanged.To}; there is nothing to change."]);
        }

        var baseGeneration = Generator.Regenerate(catalog, packs, record);
        if (baseGeneration.Errors.Count > 0)
        {
            return Failure([$"fabric {GeneratorVersion.Current} cannot regenerate {FabricFile.FileName}:", .. baseGeneration.Errors]);
        }

        Dictionary<string, IReadOnlyList<string>> project = new(record.Choices.Project);
        foreach (var (dimension, values) in changed)
        {
            project[dimension] = values;
        }

        var theirs = Generator.Regenerate(catalog, packs, record with { Choices = record.Choices with { Project = project } });
        var theirsLabel = string.Join(' ', changes.Select(change => $"{change.Dimension}={change.To}"));
        if (theirs.Errors.Count > 0)
        {
            return Failure([$"{FabricFile.FileName} with {theirsLabel} cannot be generated:", .. theirs.Errors]);
        }

        var (outcome, mergeError) = GeneratedMerge.Merge(
            repository,
            record.Name,
            baseGeneration.Files,
            theirs.Files,
            ("project", string.Join(' ', changes.Select(change => $"{change.Dimension}={change.From}")), theirsLabel));
        if (outcome is null)
        {
            return Failure([mergeError!]);
        }

        var hasOpenApiDocument = theirs.Files.Any(file => file.Path == UpgradeSummary.OpenApiDocument);
        var hasBaseApi = theirs.Files.Any(file => file.Path == UpgradeSummary.BaseApi);
        return new UpgradeResult(UpgradeSummary.WriteChange(record.Name, changes, outcome, hasOpenApiDocument, hasBaseApi), [])
        {
            Experimental = theirs.Experimental,
        };
    }

    /// <summary>
    /// The <c>--set</c> assignments as dimension and values, or every one the catalog does not let <c>fabric change</c>
    /// set, by name (ADR-0016 C2).
    /// </summary>
    private static (Dictionary<string, IReadOnlyList<string>> Changed, List<string> Refusals) Parse(IReadOnlyList<string> assignments, CatalogModel catalog)
    {
        Dictionary<string, IReadOnlyList<string>> changed = new(StringComparer.Ordinal);
        List<string> refusals = [];
        if (assignments.Count == 0)
        {
            refusals.Add("nothing to change; pass --set <dimension>=<option> for a dimension the catalog marks changeable.");
        }

        foreach (var assignment in assignments)
        {
            var separator = assignment.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                refusals.Add($"--set {assignment} is not <dimension>=<option>.");
                continue;
            }

            var dimension = assignment[..separator];
            if (!catalog.Dimensions.TryGetValue(dimension, out var known))
            {
                refusals.Add($"{dimension} is not a dimension of the catalog; fabric change sets only dimensions the catalog marks changeable (ADR-0016 C2).");
            }
            else if (!known.Changeable)
            {
                refusals.Add($"{dimension} cannot be changed after generation: the catalog does not mark it changeable (ADR-0016 C2).");
            }
            else if (!changed.TryAdd(dimension, assignment[(separator + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                refusals.Add($"{dimension} is set twice.");
            }
        }

        return (changed, refusals);
    }

    private static UpgradeResult Failure(IReadOnlyList<string> errors) => new(null, errors);
}

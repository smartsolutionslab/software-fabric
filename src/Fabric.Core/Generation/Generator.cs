using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

/// <summary>Either the files of a new project, ordered by path, or the errors that stopped it.</summary>
public sealed record GenerationResult(IReadOnlyList<RenderedFile> Files, IReadOnlyList<string> Errors);

public static class Generator
{
    /// <summary>Resolve, check rules, compose, render, and add <c>fabric.yaml</c>. Stops at the first stage that fails.</summary>
    public static GenerationResult New(
        CatalogModel catalog,
        IReadOnlyDictionary<string, PackManifest> packs,
        string? profile,
        IReadOnlyList<string> overrides)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(overrides);

        var resolution = ChoiceResolver.Resolve(catalog, profile, overrides);
        if (resolution.Choices is not { } resolved)
        {
            return Failure(resolution.Errors);
        }

        var violations = RuleEvaluator.Check(catalog, resolved);
        if (violations.Count > 0)
        {
            return Failure(violations);
        }

        var choices = new ProjectChoices(
            resolved.Project,
            resolved.ModuleDefaults,
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>());

        var composition = Composer.Compose(catalog, packs, choices);
        List<string> compositionErrors = [
            .. composition.Errors,
            .. composition.Files
                .Where(file => file.Target == FabricFile.FileName)
                .Select(file => $"{file.Pack.Option} claims {FabricFile.FileName}, which the generator writes."),
        ];
        if (compositionErrors.Count > 0)
        {
            return Failure(compositionErrors);
        }

        var rendered = Renderer.Render(catalog, composition, choices);
        if (rendered.Errors.Count > 0)
        {
            return Failure(rendered.Errors);
        }

        var fabricFile = new RenderedFile(FabricFile.FileName, FabricFile.Write(catalog, profile, choices));
        return new GenerationResult([.. rendered.Files.Append(fabricFile).OrderBy(file => file.Path, StringComparer.Ordinal)], []);
    }

    private static GenerationResult Failure(IReadOnlyList<string> errors) => new([], errors);
}

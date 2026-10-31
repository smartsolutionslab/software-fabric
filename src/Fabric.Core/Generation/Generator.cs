using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

/// <summary>Either the files of a new project, ordered by path, or the errors that stopped it.</summary>
public sealed record GenerationResult(IReadOnlyList<RenderedFile> Files, IReadOnlyList<string> Errors);

/// <summary>The files a new module creates and the updated <c>fabric.yaml</c>, or the errors that stopped it.</summary>
public sealed record ModuleAddition(IReadOnlyList<RenderedFile> Created, string? FabricYaml, IReadOnlyList<string> Errors);

public static class Generator
{
    /// <summary>
    /// Adds a module to a generated project. Only creates files: an option whose pack is new to the project and
    /// contributes project-wide content is refused, because that content would have to be merged into existing files.
    /// </summary>
    public static ModuleAddition AddModule(
        CatalogModel catalog,
        IReadOnlyDictionary<string, PackManifest> packs,
        FabricRecord project,
        string name,
        IReadOnlyList<string> overrides)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(overrides);

        if (!ModuleNames.IsValid(name))
        {
            return AdditionFailure([$"module name '{name}' must be lower-case letters, digits and hyphens, starting with a letter."]);
        }

        if (project.Choices.Modules.ContainsKey(name))
        {
            return AdditionFailure([$"module '{name}' already exists."]);
        }

        var resolution = ChoiceResolver.ResolveModule(catalog, project.Choices, overrides);
        if (resolution.Values is not { } values)
        {
            return AdditionFailure(resolution.Errors);
        }

        OrderedDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules = [];
        foreach (var (existing, existingValues) in project.Choices.Modules)
        {
            modules.Add(existing, existingValues);
        }

        modules.Add(name, values);
        var choices = project.Choices with { Modules = modules };

        var violations = RuleEvaluator.Check(catalog, choices.Project, choices.Modules);
        if (violations.Count > 0)
        {
            return AdditionFailure(violations);
        }

        var before = Composer.Compose(catalog, packs, project.Choices);
        var after = Composer.Compose(catalog, packs, choices);
        if (before.Errors.Count > 0 || after.Errors.Count > 0)
        {
            return AdditionFailure([.. before.Errors.Union(after.Errors, StringComparer.Ordinal)]);
        }

        var alreadySelected = before.Packs.Select(pack => pack.Option).ToHashSet(StringComparer.Ordinal);
        var refused = after.Packs
            .Where(pack => !alreadySelected.Contains(pack.Option))
            .Select(ProjectWideRefusal)
            .OfType<string>()
            .ToList();
        if (refused.Count > 0)
        {
            return AdditionFailure(refused);
        }

        var moduleOnly = after with { Files = [.. after.Files.Where(file => file.Module == name)] };
        var rendered = Renderer.Render(catalog, moduleOnly, choices);
        return rendered.Errors.Count > 0
            ? AdditionFailure(rendered.Errors)
            : new ModuleAddition(rendered.Files, FabricFile.Write(catalog, project.Profile, choices), []);
    }

    private static string? ProjectWideRefusal(PackManifest pack)
    {
        var projectWide = pack.Files.Where(file => !file.PerModule).Select(file => $"file {file.Target}")
            .Concat(pack.Slots.Select(slot => $"slot {slot.Slot}"))
            .ToList();

        return projectWide.Count == 0
            ? null
            : $"{pack.Dimension}={pack.OptionName} is new to this project and its pack contributes to project-wide files " +
              $"({string.Join(", ", projectWide)}); add-module only creates files, so choose it when generating the project or add it by hand.";
    }

    private static ModuleAddition AdditionFailure(IReadOnlyList<string> errors) => new([], null, errors);

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

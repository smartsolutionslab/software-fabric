using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;
using Fabric.Core.Rendering;

namespace Fabric.Core.Generation;

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

        if (!Names.IsValid(name))
        {
            return AdditionFailure([$"module name '{name}' must be lower-case letters, digits and hyphens, starting with a letter."]);
        }

        var before = Composer.Compose(catalog, packs, project.Choices);
        if (PackModules.Declarers(before.Packs).TryGetValue(name, out var declarers))
        {
            return AdditionFailure([
                $"module '{name}' is brought by {string.Join(", ", declarers)}; fabric new adds it with that pack's pinned choices, so add-module cannot add it.",
            ]);
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
        var rendered = Renderer.Render(catalog, moduleOnly, choices, project.Name);
        return rendered.Errors.Count > 0
            ? AdditionFailure(rendered.Errors)
            : new ModuleAddition(rendered.Files, FabricFile.Write(catalog, project.Name, project.Profile, choices), []);
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
        string name,
        string? profile,
        IReadOnlyList<string> overrides)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(overrides);

        if (!Names.IsValid(name))
        {
            return Failure([$"project name '{name}' must be lower-case letters, digits and hyphens, starting with a letter."]);
        }

        var resolution = ChoiceResolver.Resolve(catalog, profile, overrides);
        if (resolution.Choices is not { } resolved)
        {
            return Failure(resolution.Errors);
        }

        var choices = new ProjectChoices(
            resolved.Project,
            resolved.ModuleDefaults,
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>());

        // Only base and project-scoped packs bring modules (PackLoader), so the modules cannot change which packs bring them.
        var bringing = Composer.Select(catalog, packs, choices).Where(pack => pack.Modules.Count > 0).ToList();
        IReadOnlyList<string> violations;
        if (bringing.Count == 0)
        {
            violations = RuleEvaluator.Check(catalog, resolved);
        }
        else
        {
            var (modules, moduleErrors) = PackModules.Resolve(catalog, bringing, choices);
            if (moduleErrors.Count > 0)
            {
                return Failure(moduleErrors);
            }

            choices = choices with { Modules = modules };
            violations = NameDeclarers(RuleEvaluator.Check(catalog, resolved, modules), PackModules.Declarers(bringing));
        }

        if (violations.Count > 0)
        {
            return Failure(violations);
        }

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

        var rendered = Renderer.Render(catalog, composition, choices, name);
        if (rendered.Errors.Count > 0)
        {
            return Failure(rendered.Errors);
        }

        var fabricFile = new RenderedFile(FabricFile.FileName, FabricFile.Write(catalog, name, profile, choices));
        return new GenerationResult([.. rendered.Files.Append(fabricFile).OrderBy(file => file.Path, StringComparer.Ordinal)], []);
    }

    /// <summary>Puts the packs that bring a module in front of a rule violation reported for that module.</summary>
    private static List<string> NameDeclarers(IReadOnlyList<string> violations, Dictionary<string, List<string>> declarers) =>
        [.. violations.Select(violation => declarers
            .Where(declared => violation.StartsWith($"module '{declared.Key}': ", StringComparison.Ordinal))
            .Select(declared => $"{string.Join(", ", declared.Value)}: {violation}")
            .FirstOrDefault(violation))];

    private static GenerationResult Failure(IReadOnlyList<string> errors) => new([], errors);
}

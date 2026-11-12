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

        var settingErrors = ChoiceResolver.CheckSettings(catalog, project.Choices);
        if (settingErrors.Count > 0)
        {
            return AdditionFailure([.. settingErrors.Select(error => $"{FabricFile.FileName}: {error}")]);
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
            : new ModuleAddition(rendered.Files, FabricFile.Write(catalog, project.Name, project.Profile, choices), [])
            {
                Experimental = ExperimentalOptions.Chosen(catalog, choices),
            };
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

        return New(catalog, packs, name, profile, profile, overrides);
    }

    /// <param name="profile">The profile the choices are resolved from, if any.</param>
    /// <param name="recordedProfile">The profile <c>fabric.yaml</c> names, which a regeneration keeps even when the
    /// catalog no longer has it.</param>
    private static GenerationResult New(
        CatalogModel catalog,
        IReadOnlyDictionary<string, PackManifest> packs,
        string name,
        string? profile,
        string? recordedProfile,
        IReadOnlyList<string> overrides)
    {

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
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>())
        {
            Settings = resolved.Settings,
        };

        // Only base and project-scoped packs bring modules (PackLoader), so the modules cannot change which packs bring them.
        // Checked as fabric validate and the catalog lint check it, without packs, so the three agree on every profile.
        var violations = RuleEvaluator.Check(catalog, resolved);
        if (violations.Count > 0)
        {
            return Failure(violations);
        }

        var bringing = Composer.Select(catalog, packs, choices).Where(pack => pack.Modules.Count > 0).ToList();
        if (bringing.Count > 0)
        {
            var (modules, moduleErrors) = PackModules.Resolve(catalog, bringing, choices);
            if (moduleErrors.Count > 0)
            {
                return Failure(moduleErrors);
            }

            choices = choices with { Modules = modules };
            var moduleViolations = RuleEvaluator.Check(catalog, resolved, modules);
            if (moduleViolations.Count > 0)
            {
                return Failure(NameDeclarers(moduleViolations, PackModules.Declarers(bringing)));
            }
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

        var fabricFile = new RenderedFile(FabricFile.FileName, FabricFile.Write(catalog, name, recordedProfile, choices));
        return new GenerationResult([.. rendered.Files.Append(fabricFile).OrderBy(file => file.Path, StringComparer.Ordinal)], [])
        {
            Experimental = ExperimentalOptions.Chosen(catalog, choices),
        };
    }

    /// <summary>
    /// Writes a project again from its <c>fabric.yaml</c> alone (ADR-0016 E4), with the calls <see cref="Regeneration"/>
    /// spells out: <see cref="New"/>, then <see cref="AddModule"/> for each module <c>new</c> did not bring, in order.
    /// A choice set this catalog rejects fails with the catalog's own errors, as <c>fabric new</c> would.
    /// </summary>
    public static GenerationResult Regenerate(CatalogModel catalog, IReadOnlyDictionary<string, PackManifest> packs, FabricRecord project)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(project);

        var regeneration = Regeneration.From(project);
        // Every recorded choice is an override, so the profile only fills what fabric.yaml does not record. A profile the
        // catalog no longer has cannot do that, and the record still names it.
        var profile = regeneration.Profile is { } named && catalog.Profiles.ContainsKey(named) ? named : null;
        var generated = New(catalog, packs, regeneration.Name, profile, regeneration.Profile, regeneration.Overrides);
        if (generated.Errors.Count > 0)
        {
            return generated;
        }

        var files = generated.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var experimental = generated.Experimental;
        foreach (var module in regeneration.Modules)
        {
            var current = FabricFile.Read(new StringReader(files[FabricFile.FileName].Content), FabricFile.FileName);
            if (current.Choices.Modules.TryGetValue(module.Name, out var brought))
            {
                var recorded = project.Choices.Modules[module.Name];
                if (!SameValues(recorded, brought))
                {
                    return Failure([
                        $"module '{module.Name}' records {Describe(recorded)}, but a pack brings it with {Describe(brought)}; " +
                        "a module's choices are fixed (ADR-0001), so it cannot be regenerated.",
                    ]);
                }

                continue;
            }

            var addition = AddModule(catalog, packs, current, module.Name, module.Overrides);
            if (addition.FabricYaml is not { } fabricYaml)
            {
                return Failure([.. addition.Errors.Select(error => $"add-module {module.Name}: {error}")]);
            }

            if (addition.Created.FirstOrDefault(file => files.ContainsKey(file.Path)) is { } existing)
            {
                return Failure([$"add-module {module.Name}: {existing.Path} already exists; add-module does not overwrite files."]);
            }

            foreach (var file in addition.Created)
            {
                files.Add(file.Path, file);
            }

            files[FabricFile.FileName] = new RenderedFile(FabricFile.FileName, fabricYaml);
            experimental = addition.Experimental;
        }

        return new GenerationResult([.. files.Values.OrderBy(file => file.Path, StringComparer.Ordinal)], []) { Experimental = experimental };
    }

    private static bool SameValues(IReadOnlyDictionary<string, IReadOnlyList<string>> first, IReadOnlyDictionary<string, IReadOnlyList<string>> second) =>
        first.Count == second.Count && first.All(entry => second.TryGetValue(entry.Key, out var other) && other.SequenceEqual(entry.Value));

    private static string Describe(IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        string.Join(", ", values.Select(value => $"{value.Key}={string.Join(',', value.Value)}"));

    /// <summary>Puts the packs that bring a module in front of a rule violation reported for that module.</summary>
    private static List<string> NameDeclarers(IReadOnlyList<string> violations, Dictionary<string, List<string>> declarers) =>
        [.. violations.Select(violation => declarers
            .Where(declared => violation.StartsWith($"module '{declared.Key}': ", StringComparison.Ordinal))
            .Select(declared => $"{string.Join(", ", declared.Value)}: {violation}")
            .FirstOrDefault(violation))];

    private static GenerationResult Failure(IReadOnlyList<string> errors) => new([], errors);
}

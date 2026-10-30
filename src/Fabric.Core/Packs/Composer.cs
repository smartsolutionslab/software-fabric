using System.Text.RegularExpressions;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Packs;

/// <summary>
/// Decides which packs a project uses and what each contributes (ADR-0002): the files every pack owns, expanded
/// per module where asked, and the ordered contributions to every declared slot. It renders nothing.
/// </summary>
public static partial class Composer
{
    private static readonly string?[] ProjectOnly = [null];

    public static Composition Compose(CatalogModel catalog, IReadOnlyDictionary<string, PackManifest> packs, ProjectChoices choices)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(choices);

        List<string> errors = [];
        var selected = Select(catalog, packs, choices, errors);
        var files = ComposeFiles(catalog, selected, choices, errors);
        var slots = ComposeSlots(selected, errors);

        return new Composition(selected, files, slots, errors);
    }

    /// <summary>Base packs first, then one pack per chosen option in catalog order.</summary>
    private static List<PackManifest> Select(
        CatalogModel catalog,
        IReadOnlyDictionary<string, PackManifest> packs,
        ProjectChoices choices,
        List<string> errors)
    {
        List<PackManifest> selected = [.. packs.Values.Where(pack => pack.IsBase).OrderBy(pack => pack.Option, StringComparer.Ordinal)];

        foreach (var dimension in catalog.Dimensions.Values)
        {
            var chosen = dimension.Scope == DimensionScope.Project
                ? Chosen(choices.Project, dimension.Id)
                : Chosen(choices.ModuleDefaults, dimension.Id).Concat(choices.Modules.Values.SelectMany(module => Chosen(module, dimension.Id)));

            foreach (var option in chosen.Distinct(StringComparer.Ordinal))
            {
                if (packs.TryGetValue($"{dimension.Id}.{option}", out var pack))
                {
                    selected.Add(pack);
                }
                else
                {
                    errors.Add($"no pack for {dimension.Id}={option}.");
                }
            }
        }

        return selected;
    }

    private static List<ComposedFile> ComposeFiles(
        CatalogModel catalog,
        List<PackManifest> selected,
        ProjectChoices choices,
        List<string> errors)
    {
        List<ComposedFile> files = [];
        Dictionary<string, PackManifest> claims = new(StringComparer.Ordinal);

        foreach (var pack in selected)
        {
            foreach (var file in pack.Files)
            {
                var usesModuleName = ModuleNamePattern().IsMatch(file.Target);
                if (file.PerModule && !usesModuleName)
                {
                    errors.Add($"{pack.Option}: per-module target '{file.Target}' must contain {{{{ module.name }}}}.");
                    continue;
                }

                if (!file.PerModule && usesModuleName)
                {
                    errors.Add($"{pack.Option}: target '{file.Target}' uses {{{{ module.name }}}}, but the file is not per-module.");
                    continue;
                }

                IEnumerable<string?> modules = file.PerModule ? ModulesUsing(catalog, pack, choices) : ProjectOnly;
                foreach (var module in modules)
                {
                    var target = module is null ? file.Target : ModuleNamePattern().Replace(file.Target, module);
                    if (claims.TryGetValue(target, out var owner))
                    {
                        errors.Add($"{target} is claimed by both {owner.Option} and {pack.Option}.");
                        continue;
                    }

                    claims.Add(target, pack);
                    files.Add(new ComposedFile(pack, file, target, module));
                }
            }
        }

        return files;
    }

    /// <summary>A module option's per-module files render for the modules that chose it; any other pack's for every module.</summary>
    private static IEnumerable<string?> ModulesUsing(CatalogModel catalog, PackManifest pack, ProjectChoices choices) =>
        catalog.Dimensions.TryGetValue(pack.Dimension, out var dimension) && dimension.Scope == DimensionScope.Module
            ? choices.Modules.Where(module => Chosen(module.Value, pack.Dimension).Contains(pack.OptionName)).Select(module => module.Key)
            : choices.Modules.Keys;

    private static OrderedDictionary<string, IReadOnlyList<SlotEntry>> ComposeSlots(List<PackManifest> selected, List<string> errors)
    {
        OrderedDictionary<string, (PackManifest Owner, SlotDeclaration Declaration, List<SlotEntry> Entries)> slots = [];
        foreach (var pack in selected)
        {
            foreach (var declaration in pack.DeclaredSlots)
            {
                if (slots.TryGetValue(declaration.Slot, out var existing))
                {
                    errors.Add($"slot '{declaration.Slot}' is declared by both {existing.Owner.Option} and {pack.Option}.");
                    continue;
                }

                slots.Add(declaration.Slot, (pack, declaration, []));
            }
        }

        foreach (var pack in selected)
        {
            foreach (var contribution in pack.Slots)
            {
                if (slots.TryGetValue(contribution.Slot, out var slot))
                {
                    slot.Entries.Add(new SlotEntry(pack, contribution));
                }
                else
                {
                    errors.Add($"{pack.Option} contributes to slot '{contribution.Slot}', which no selected pack declares.");
                }
            }
        }

        OrderedDictionary<string, IReadOnlyList<SlotEntry>> result = [];
        foreach (var (name, (_, declaration, entries)) in slots)
        {
            List<SlotEntry> ordered = [.. entries
                .OrderBy(entry => entry.Contribution.Order)
                .ThenBy(entry => entry.Pack.Option, StringComparer.Ordinal)];
            CheckCardinality(declaration, ordered, errors);
            result.Add(name, ordered);
        }

        return result;
    }

    private static void CheckCardinality(SlotDeclaration declaration, List<SlotEntry> entries, List<string> errors)
    {
        var (wording, violated) = declaration.Cardinality switch
        {
            SlotCardinality.ExactlyOne => ("exactly one", entries.Count != 1),
            SlotCardinality.AtMostOne => ("at most one", entries.Count > 1),
            _ => (string.Empty, false),
        };

        if (violated)
        {
            var contributors = entries.Count == 0 ? string.Empty : $": {string.Join(", ", entries.Select(entry => entry.Pack.Option))}";
            errors.Add($"slot '{declaration.Slot}' takes {wording} contribution, got {entries.Count}{contributors}.");
        }
    }

    private static IReadOnlyList<string> Chosen(IReadOnlyDictionary<string, IReadOnlyList<string>> values, string dimension) =>
        values.TryGetValue(dimension, out var chosen) ? chosen : [];

    [GeneratedRegex(@"\{\{\s*module\.name\s*\}\}")]
    private static partial Regex ModuleNamePattern();
}

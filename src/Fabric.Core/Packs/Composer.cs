using System.Text.RegularExpressions;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Core.Packs;

/// <summary>
/// Decides which packs a project uses and what each contributes (ADR-0002): the files every pack owns, expanded
/// per module where asked, and the ordered contributions to every declared slot. It renders nothing.
/// </summary>
public static partial class Composer
{
    /// <summary>
    /// Contributions are Scriban code (function definitions), not output: the renderer makes them available to every
    /// template instead of rendering the slot.
    /// </summary>
    public const string TemplateFunctionsSlot = "templates.functions";

    /// <summary>Filled by the generator with every selected pack's <c>agents.agents-md</c>.</summary>
    public const string AgentSectionsSlot = "agents.sections";

    /// <summary>Followed by a role; filled by the generator with every selected pack's brief for that role.</summary>
    public const string AgentBriefSlotPrefix = "agents.brief.";

    private static readonly string?[] ProjectOnly = [null];

    public static Composition Compose(CatalogModel catalog, IReadOnlyDictionary<string, PackManifest> packs, ProjectChoices choices)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(choices);

        List<string> errors = [];
        var selected = Select(catalog, packs, choices, errors);
        var files = ComposeFiles(catalog, selected, choices, errors);
        var (slots, separators) = ComposeSlots(selected, errors);

        return new Composition(selected, files, slots, errors, separators);
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
                var usesModule = ModulePlaceholder().IsMatch(file.Target);
                if (file.PerModule && !usesModule)
                {
                    errors.Add($"{pack.Option}: per-module target '{file.Target}' must contain {{{{ module.name }}}} or {{{{ module.namespace }}}}.");
                    continue;
                }

                if (!file.PerModule && usesModule)
                {
                    errors.Add($"{pack.Option}: target '{file.Target}' uses the module, but the file is not per-module.");
                    continue;
                }

                IEnumerable<string?> modules = file.PerModule ? ModulesUsing(catalog, pack, choices) : ProjectOnly;
                foreach (var module in modules)
                {
                    var target = module is null ? file.Target : ModulePlaceholder().Replace(file.Target, match => match.Groups[1].Value == "name" ? module : Names.ToPascalCase(module));
                    Claim(claims, files, new ComposedFile(pack, file, target, module), errors);
                }
            }
        }

        foreach (var (pack, index) in selected.Select((pack, index) => (pack, index)))
        {
            var target = $"docs/adr/{index + 1:0000}-{pack.Option.Replace('.', '-')}.md";
            Claim(claims, files, new ComposedFile(pack, new PackFile(pack.Adr, target, PerModule: false), target, null), errors);
        }

        return files;
    }

    private static void Claim(Dictionary<string, PackManifest> claims, List<ComposedFile> files, ComposedFile file, List<string> errors)
    {
        if (claims.TryGetValue(file.Target, out var owner))
        {
            errors.Add($"{file.Target} is claimed by both {owner.Option} and {file.Pack.Option}.");
            return;
        }

        claims.Add(file.Target, file.Pack);
        files.Add(file);
    }

    /// <summary>A module option's per-module files render for the modules that chose it; any other pack's for every module.</summary>
    private static IEnumerable<string?> ModulesUsing(CatalogModel catalog, PackManifest pack, ProjectChoices choices) =>
        catalog.Dimensions.TryGetValue(pack.Dimension, out var dimension) && dimension.Scope == DimensionScope.Module
            ? choices.Modules.Where(module => Chosen(module.Value, pack.Dimension).Contains(pack.OptionName)).Select(module => module.Key)
            : choices.Modules.Keys;

    private static (OrderedDictionary<string, IReadOnlyList<SlotEntry>> Slots, Dictionary<string, string> Separators) ComposeSlots(
        List<PackManifest> selected,
        List<string> errors)
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
                if (IsGeneratorFilled(contribution.Slot))
                {
                    errors.Add($"{pack.Option} contributes to slot '{contribution.Slot}', which the generator fills from each pack's agents section.");
                }
                else if (slots.TryGetValue(contribution.Slot, out var slot))
                {
                    slot.Entries.Add(new SlotEntry(pack, contribution));
                }
                else
                {
                    errors.Add($"{pack.Option} contributes to slot '{contribution.Slot}', which no selected pack declares.");
                }
            }
        }

        foreach (var (pack, index) in selected.Select((pack, index) => (pack, index)))
        {
            if (slots.TryGetValue(AgentSectionsSlot, out var sections))
            {
                sections.Entries.Add(new SlotEntry(pack, new SlotContribution(AgentSectionsSlot, pack.Agents.AgentsMd, index)));
            }

            foreach (var (role, brief) in pack.Agents.Briefs)
            {
                if (slots.TryGetValue(AgentBriefSlotPrefix + role, out var briefs))
                {
                    briefs.Entries.Add(new SlotEntry(pack, new SlotContribution(AgentBriefSlotPrefix + role, brief, index)));
                }
            }
        }

        OrderedDictionary<string, IReadOnlyList<SlotEntry>> result = [];
        Dictionary<string, string> separators = [];
        foreach (var (name, (_, declaration, entries)) in slots)
        {
            List<SlotEntry> ordered = [.. entries
                .OrderBy(entry => entry.Contribution.Order)
                .ThenBy(entry => entry.Pack.Option, StringComparer.Ordinal)];
            CheckCardinality(declaration, ordered, errors);
            result.Add(name, ordered);
            if (declaration.Separator is { } separator)
            {
                separators.Add(name, separator);
            }
        }

        return (result, separators);
    }

    private static bool IsGeneratorFilled(string slot) =>
        slot == AgentSectionsSlot || slot.StartsWith(AgentBriefSlotPrefix, StringComparison.Ordinal);

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

    [GeneratedRegex(@"\{\{\s*module\.(name|namespace)\s*\}\}")]
    private static partial Regex ModulePlaceholder();
}

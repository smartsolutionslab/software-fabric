using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;

namespace Fabric.Core.Generation;

/// <summary>
/// The modules the selected packs bring (ADR-0005), resolved as <c>add-module</c> resolves a module: the pinned
/// choices first, then the project's module defaults.
/// </summary>
internal static class PackModules
{
    /// <summary>Which selected packs bring each module, by module name.</summary>
    public static Dictionary<string, List<string>> Declarers(IEnumerable<PackManifest> selected)
    {
        Dictionary<string, List<string>> declarers = new(StringComparer.Ordinal);
        foreach (var pack in selected)
        {
            foreach (var module in pack.Modules.Keys)
            {
                if (!declarers.TryGetValue(module, out var packs))
                {
                    packs = [];
                    declarers.Add(module, packs);
                }

                packs.Add(pack.Option);
            }
        }

        return declarers;
    }

    /// <summary>Every declared module with its resolved values, in name order, or the errors naming the packs.</summary>
    public static (OrderedDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Modules, List<string> Errors) Resolve(
        CatalogModel catalog,
        IEnumerable<PackManifest> selected,
        ProjectChoices choices)
    {
        Dictionary<string, (List<string> Packs, OrderedDictionary<string, (string Pack, IReadOnlyList<string> Options)> Pins)> declared =
            new(StringComparer.Ordinal);
        List<string> errors = [];

        foreach (var pack in selected)
        {
            foreach (var (module, pins) in pack.Modules)
            {
                if (!declared.TryGetValue(module, out var declaration))
                {
                    declaration = ([], []);
                    declared.Add(module, declaration);
                }

                declaration.Packs.Add(pack.Option);
                foreach (var (dimension, options) in pins)
                {
                    if (!declaration.Pins.TryGetValue(dimension, out var earlier))
                    {
                        declaration.Pins.Add(dimension, (pack.Option, options));
                    }
                    else if (!earlier.Options.SequenceEqual(options, StringComparer.Ordinal))
                    {
                        errors.Add(
                            $"module '{module}': {pack.Option} pins {dimension}={string.Join(",", options)}, " +
                            $"but {earlier.Pack} pins {dimension}={string.Join(",", earlier.Options)}.");
                    }
                }
            }
        }

        OrderedDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules = [];
        foreach (var (module, (packs, pins)) in declared.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var overrides = pins.Select(pin => $"{pin.Key}={string.Join(",", pin.Value.Options)}").ToList();
            var resolution = ChoiceResolver.ResolveModule(catalog, choices, overrides);
            if (resolution.Values is { } values)
            {
                modules.Add(module, values);
            }
            else
            {
                errors.AddRange(resolution.Errors.Select(error => $"{string.Join(", ", packs)}: module '{module}': {error}"));
            }
        }

        return (modules, errors);
    }
}

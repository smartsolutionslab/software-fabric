using Fabric.Core.Catalog;

namespace Fabric.Core.Choices;

/// <summary>
/// Turns a profile plus <c>dimension=option</c> overrides into a complete choice set: explicit values first,
/// then defaults for every dimension whose <c>active-when</c> holds. Rule checks (requires/conflicts) are not
/// done here.
/// </summary>
public static class ChoiceResolver
{
    public static ChoiceResolution Resolve(CatalogModel catalog, string? profile, IReadOnlyList<string> overrides)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(overrides);

        List<string> errors = [];
        OrderedDictionary<string, IReadOnlyList<string>> explicitValues = [];

        if (profile is not null)
        {
            ApplyProfile(catalog, profile, explicitValues, errors);
        }

        foreach (var setting in overrides)
        {
            ApplyOverride(setting, explicitValues, errors);
        }

        if (errors.Count == 0)
        {
            Validate(catalog, explicitValues, errors);
        }

        if (errors.Count > 0)
        {
            return Failure(errors);
        }

        var defaults = SettleDefaults(catalog, explicitValues, errors);
        if (errors.Count > 0)
        {
            return Failure(errors);
        }

        var values = Merge(explicitValues, defaults);
        CheckActivity(catalog, explicitValues, values, errors);
        if (errors.Count > 0)
        {
            return Failure(errors);
        }

        OrderedDictionary<string, IReadOnlyList<string>> project = [];
        OrderedDictionary<string, IReadOnlyList<string>> moduleDefaults = [];
        foreach (var dimension in catalog.Dimensions.Values)
        {
            if (values.TryGetValue(dimension.Id, out var chosen))
            {
                (dimension.Scope == DimensionScope.Project ? project : moduleDefaults).Add(dimension.Id, chosen);
            }
        }

        return new ChoiceResolution(new ResolvedChoices(project, moduleDefaults), []);
    }

    private static ChoiceResolution Failure(List<string> errors) => new(null, errors);

    private static void ApplyProfile(
        CatalogModel catalog,
        string profile,
        OrderedDictionary<string, IReadOnlyList<string>> values,
        List<string> errors)
    {
        if (!catalog.Profiles.TryGetValue(profile, out var current))
        {
            errors.Add($"unknown profile '{profile}'.");
            return;
        }

        List<Profile> chain = [current];
        while (current.Extends is { } parent)
        {
            if (chain.Any(link => link.Id == parent))
            {
                errors.Add($"profile '{profile}' extends itself: {string.Join(" -> ", chain.Select(link => link.Id))} -> {parent}.");
                return;
            }

            if (!catalog.Profiles.TryGetValue(parent, out current))
            {
                errors.Add($"profile '{chain[^1].Id}' extends unknown profile '{parent}'.");
                return;
            }

            chain.Add(current);
        }

        foreach (var link in Enumerable.Reverse(chain))
        {
            foreach (var (dimension, chosen) in link.Settings)
            {
                values[dimension] = chosen;
            }
        }
    }

    private static void ApplyOverride(string setting, OrderedDictionary<string, IReadOnlyList<string>> values, List<string> errors)
    {
        var separator = setting.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            errors.Add($"override '{setting}' must look like <dimension>=<option>.");
            return;
        }

        var options = setting[(separator + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        values[setting[..separator].Trim()] = options;
    }

    private static void Validate(CatalogModel catalog, OrderedDictionary<string, IReadOnlyList<string>> values, List<string> errors)
    {
        foreach (var (dimensionId, chosen) in values)
        {
            if (!catalog.Dimensions.TryGetValue(dimensionId, out var dimension))
            {
                errors.Add($"unknown dimension '{dimensionId}'.");
                continue;
            }

            if (!dimension.Multi && chosen.Count != 1)
            {
                errors.Add($"{dimensionId} takes one option, got {chosen.Count}: {string.Join(", ", chosen)}.");
                continue;
            }

            foreach (var option in chosen.Where(option => !dimension.Options.ContainsKey(option)))
            {
                errors.Add($"'{option}' is not an option of {dimensionId}; options: {string.Join(", ", dimension.Options.Keys)}.");
            }
        }
    }

    /// <summary>
    /// Defaults can switch other dimensions on or off, so they are recomputed until the set stops changing.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> SettleDefaults(
        CatalogModel catalog,
        OrderedDictionary<string, IReadOnlyList<string>> explicitValues,
        List<string> errors)
    {
        Dictionary<string, IReadOnlyList<string>> defaults = [];
        for (var round = 0; round <= catalog.Dimensions.Count; round++)
        {
            var view = Merge(explicitValues, defaults);
            Dictionary<string, IReadOnlyList<string>> next = [];
            foreach (var dimension in catalog.Dimensions.Values)
            {
                if (!explicitValues.ContainsKey(dimension.Id) && dimension.Default is { } value && IsActive(dimension, view))
                {
                    next.Add(dimension.Id, value);
                }
            }

            if (next.Keys.ToHashSet().SetEquals(defaults.Keys))
            {
                return next;
            }

            defaults = next;
        }

        errors.Add("defaults do not settle: active-when conditions switch each other on and off.");
        return defaults;
    }

    private static void CheckActivity(
        CatalogModel catalog,
        OrderedDictionary<string, IReadOnlyList<string>> explicitValues,
        Dictionary<string, IReadOnlyList<string>> values,
        List<string> errors)
    {
        foreach (var dimension in catalog.Dimensions.Values)
        {
            var active = IsActive(dimension, values);
            if (!active && explicitValues.TryGetValue(dimension.Id, out var chosen))
            {
                errors.Add(
                    $"{dimension.Id} is set to {string.Join(", ", chosen)}, but it only applies when {dimension.ActiveWhen}, " +
                    $"and {ConditionEvaluator.DescribeActual(dimension.ActiveWhen!, values)}.");
            }
            else if (active && !values.ContainsKey(dimension.Id))
            {
                errors.Add($"{dimension.Id} has no value and no default; set it with {dimension.Id}=<option>.");
            }
        }
    }

    private static bool IsActive(Dimension dimension, IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        dimension.ActiveWhen is null || ConditionEvaluator.Holds(dimension.ActiveWhen, values);

    private static Dictionary<string, IReadOnlyList<string>> Merge(
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> first,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> second) =>
        first.Concat(second).ToDictionary();
}

using Fabric.Core.Catalog;

namespace Fabric.Core.Choices;

/// <summary>
/// The settings chosen options declare (ADR-0013), keyed <c>&lt;dimension&gt;.&lt;option&gt;.&lt;setting&gt;</c>: read from
/// <c>--set</c>, settled to their defaults, and checked again when <c>fabric.yaml</c> is read back.
/// </summary>
internal static class OptionSettings
{
    /// <param name="Value">The text after <c>=</c>, as given, for messages that quote it.</param>
    internal sealed record Assignment(string Key, Dimension Dimension, CatalogOption Option, string Setting, string Value);

    /// <summary>
    /// An override whose key is not a dimension but names one of its options and something after it; whether that
    /// option declares the setting is checked by <see cref="Read"/>.
    /// </summary>
    public static Assignment? Find(CatalogModel catalog, string text)
    {
        var separator = text.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var key = text[..separator].Trim();
        return IsSettingKey(catalog, key, out var dimension, out var option, out var setting)
            ? new Assignment(key, dimension, option, setting, text[(separator + 1)..].Trim())
            : null;
    }

    /// <summary>The value of each assignment whose option declares the setting and whose value is in range.</summary>
    public static Dictionary<string, decimal> Read(IEnumerable<Assignment> assignments, List<string> errors)
    {
        Dictionary<string, decimal> values = [];
        foreach (var assignment in assignments)
        {
            var (key, dimension, option, settingId, text) = assignment;
            if (!option.Settings.TryGetValue(settingId, out var setting))
            {
                errors.Add($"unknown setting '{settingId}' of {dimension.Id}={option.Id}; " +
                    (option.Settings.Count == 0 ? "it has no settings." : $"its settings: {string.Join(", ", option.Settings.Keys)}."));
            }
            else if (!SettingNumber.TryParse(text, out var value))
            {
                errors.Add($"{key} must be a number between {Range(setting)}, got '{text}'.");
            }
            else if (OutOfRange(setting, value))
            {
                errors.Add($"{key} is {text}, but it must be between {Range(setting)}.");
            }
            else
            {
                values[key] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// Every chosen project option's settings, in catalog order: the value set, or the default. A value set for an
    /// option that is not chosen is an error naming the option it needs.
    /// </summary>
    public static Dictionary<string, decimal> Settle(
        CatalogModel catalog,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        IReadOnlyList<Assignment> assignments,
        IReadOnlyDictionary<string, decimal> set,
        List<string> errors)
    {
        foreach (var assignment in assignments.Where(assignment => set.ContainsKey(assignment.Key) && !IsChosen(assignment.Dimension, assignment.Option, values)))
        {
            errors.Add(NotChosen(assignment.Key, assignment.Value, assignment.Dimension, assignment.Option, values));
        }

        Dictionary<string, decimal> settled = [];
        foreach (var dimension in catalog.Dimensions.Values.Where(dimension => dimension.Scope == DimensionScope.Project))
        {
            foreach (var option in dimension.Options.Values.Where(option => IsChosen(dimension, option, values)))
            {
                foreach (var setting in option.Settings.Values)
                {
                    var key = KeyOf(dimension, option, setting.Id);
                    settled[key] = set.TryGetValue(key, out var value) ? value : setting.Default;
                }
            }
        }

        return settled;
    }

    /// <summary>The settings <c>fabric.yaml</c> records, against the catalog and the project's own choices.</summary>
    public static IReadOnlyList<string> Check(CatalogModel catalog, ProjectChoices choices)
    {
        List<string> errors = [];
        foreach (var (key, value) in choices.Settings)
        {
            if (!IsSettingKey(catalog, key, out var dimension, out var option, out var settingId) || !option.Settings.TryGetValue(settingId, out var setting))
            {
                errors.Add($"{key} is not a setting any option in the catalog declares.");
            }
            else if (!IsChosen(dimension, option, choices.Project))
            {
                errors.Add(NotChosen(key, SettingNumber.Format(value), dimension, option, choices.Project));
            }
            else if (OutOfRange(setting, value))
            {
                errors.Add($"{key} is {SettingNumber.Format(value)}, but it must be between {Range(setting)}.");
            }
        }

        return errors;
    }

    private static bool IsSettingKey(CatalogModel catalog, string key, out Dimension dimension, out CatalogOption option, out string setting)
    {
        (dimension, option, setting) = (null!, null!, string.Empty);
        var last = key.LastIndexOf('.');
        var optionDot = last > 0 ? key.LastIndexOf('.', last - 1) : -1;
        if (catalog.Dimensions.ContainsKey(key) || optionDot <= 0 || !catalog.Dimensions.TryGetValue(key[..optionDot], out var found))
        {
            return false;
        }

        if (!found.Options.TryGetValue(key[(optionDot + 1)..last], out var foundOption))
        {
            return false;
        }

        (dimension, option, setting) = (found, foundOption, key[(last + 1)..]);
        return true;
    }

    private static string KeyOf(Dimension dimension, CatalogOption option, string setting) => $"{dimension.Id}.{option.Id}.{setting}";

    private static bool IsChosen(Dimension dimension, CatalogOption option, IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        values.TryGetValue(dimension.Id, out var chosen) && chosen.Contains(option.Id);

    private static string NotChosen(
        string key,
        string value,
        Dimension dimension,
        CatalogOption option,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        var needed = new Condition(dimension.Id, dimension.Multi ? ConditionOperator.Has : ConditionOperator.Equals, [option.Id]);
        return $"{key} is set to {value}, but it only applies when {needed}, and {ConditionEvaluator.DescribeActual(needed, values)}.";
    }

    private static bool OutOfRange(OptionSetting setting, decimal value) => value < setting.Minimum || value > setting.Maximum;

    private static string Range(OptionSetting setting) => $"{SettingNumber.Format(setting.Minimum)} and {SettingNumber.Format(setting.Maximum)}";
}

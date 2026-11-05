using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Core.Catalog;

/// <summary>
/// Checks that the catalog is consistent with itself: every condition names real dimensions and options with an
/// operator that fits, every default is an option, every default-from can give one, and every profile resolves and satisfies every rule.
/// </summary>
public static class CatalogLinter
{
    public static IReadOnlyList<string> Check(CatalogModel catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        List<string> problems = [];
        foreach (var dimension in catalog.Dimensions.Values)
        {
            CheckDimension(catalog, dimension, problems);
        }

        foreach (var profile in catalog.Profiles.Keys)
        {
            CheckProfile(catalog, profile, problems);
        }

        return problems;
    }

    private static void CheckDimension(CatalogModel catalog, Dimension dimension, List<string> problems)
    {
        if (dimension.Options.Count == 0)
        {
            problems.Add($"{dimension.Id}: has no options.");
        }

        if (dimension.Default is { } defaults)
        {
            if (!dimension.Multi && defaults.Count != 1)
            {
                problems.Add($"{dimension.Id}: default has {defaults.Count} values, but the dimension is single-valued.");
            }

            problems.AddRange(defaults
                .Where(option => !dimension.Options.ContainsKey(option))
                .Select(option => $"{dimension.Id}: default '{option}' is not an option."));
        }

        if (dimension.DefaultFrom is { } defaultFrom)
        {
            CheckDefaultFrom(catalog, dimension, defaultFrom, problems);
        }

        if (dimension.ActiveWhen is { } activeWhen)
        {
            var subject = $"{dimension.Id} active-when";
            if (activeWhen.Dimension == dimension.Id)
            {
                problems.Add($"{subject}: condition '{activeWhen}' refers to its own dimension.");
            }

            CheckCondition(catalog, subject, activeWhen, problems);
        }

        foreach (var option in dimension.Options.Values)
        {
            foreach (var condition in option.Requires.Concat(option.Conflicts))
            {
                CheckCondition(catalog, $"{dimension.Id}={option.Id}", condition, problems);
            }

            foreach (var setting in option.Settings.Values)
            {
                CheckSetting(dimension, option, setting, problems);
            }
        }
    }

    /// <summary>A setting's key in fabric.yaml is <c>&lt;dimension&gt;.&lt;option&gt;.&lt;setting&gt;</c>, so its id holds no dot.</summary>
    private static void CheckSetting(Dimension dimension, CatalogOption option, OptionSetting setting, List<string> problems)
    {
        var subject = $"{dimension.Id}={option.Id}: setting '{setting.Id}'";
        if (!Names.IsValid(setting.Id))
        {
            problems.Add($"{subject} must be lower-case letters, digits and hyphens, starting with a letter.");
        }

        if (dimension.Scope != DimensionScope.Project)
        {
            problems.Add($"{subject} is on a module-scoped dimension; settings are project-wide, so only project-scoped options declare them.");
        }

        var (minimum, maximum) = (SettingNumber.Format(setting.Minimum), SettingNumber.Format(setting.Maximum));
        if (setting.Minimum > setting.Maximum)
        {
            problems.Add($"{subject} has minimum {minimum} above its maximum {maximum}.");
        }
        else if (setting.Default < setting.Minimum || setting.Default > setting.Maximum)
        {
            problems.Add($"{subject} defaults to {SettingNumber.Format(setting.Default)}, outside its range {minimum} to {maximum}.");
        }
    }

    private static void CheckDefaultFrom(CatalogModel catalog, Dimension dimension, string defaultFrom, List<string> problems)
    {
        if (dimension.Default is not null)
        {
            problems.Add($"{dimension.Id}: has both default and default-from; use one.");
        }

        if (dimension.Multi)
        {
            problems.Add($"{dimension.Id}: default-from is only for a single-valued dimension.");
        }

        if (defaultFrom == dimension.Id)
        {
            problems.Add($"{dimension.Id}: default-from names its own dimension.");
        }
        else if (!catalog.Dimensions.TryGetValue(defaultFrom, out var followed))
        {
            problems.Add($"{dimension.Id}: default-from names unknown dimension '{defaultFrom}'.");
        }
        else if (followed.Multi)
        {
            problems.Add($"{dimension.Id}: default-from names multi-valued {followed.Id}; it can follow only a single-valued dimension.");
        }
        else if (!followed.Options.Keys.Any(dimension.Options.ContainsKey))
        {
            problems.Add($"{dimension.Id}: default-from names {followed.Id}, which shares no option with it, so it never gives a default.");
        }
    }

    private static void CheckCondition(CatalogModel catalog, string subject, Condition condition, List<string> problems)
    {
        var prefix = $"{subject}: condition '{condition}'";
        if (!catalog.Dimensions.TryGetValue(condition.Dimension, out var target))
        {
            problems.Add($"{prefix} names unknown dimension '{condition.Dimension}'.");
            return;
        }

        problems.AddRange(condition.Options
            .Where(option => !target.Options.ContainsKey(option))
            .Select(option => $"{prefix} names '{option}', which is not an option of {target.Id}."));

        if (target.Multi && condition.Operator != ConditionOperator.Has)
        {
            problems.Add($"{prefix} uses '{Symbol(condition.Operator)}' on multi-valued {target.Id}; use has.");
        }
        else if (!target.Multi && condition.Operator == ConditionOperator.Has)
        {
            problems.Add($"{prefix} uses 'has' on single-valued {target.Id}; use ==, != or in.");
        }
    }

    private static void CheckProfile(CatalogModel catalog, string profile, List<string> problems)
    {
        var resolution = ChoiceResolver.Resolve(catalog, profile, []);
        var findings = resolution.Choices is { } choices
            ? RuleEvaluator.Check(catalog, choices)
            : resolution.Errors;

        problems.AddRange(findings.Select(finding => $"profile '{profile}': {finding}"));
    }

    private static string Symbol(ConditionOperator conditionOperator) => conditionOperator switch
    {
        ConditionOperator.Equals => "==",
        ConditionOperator.NotEquals => "!=",
        ConditionOperator.In => "in",
        ConditionOperator.Has => "has",
        _ => throw new InvalidOperationException($"Unknown operator {conditionOperator}."),
    };
}

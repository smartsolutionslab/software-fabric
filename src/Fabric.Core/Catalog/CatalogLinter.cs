using Fabric.Core.Choices;

namespace Fabric.Core.Catalog;

/// <summary>
/// Checks that the catalog is consistent with itself: every condition names real dimensions and options with an
/// operator that fits, every default is an option, and every profile resolves and satisfies every rule.
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

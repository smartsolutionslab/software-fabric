using Fabric.Core.Catalog;

namespace Fabric.Core.Choices;

/// <summary>
/// Checks every chosen option's <c>requires</c> and <c>conflicts</c>. Module options are checked once per
/// module against that module's values plus the project's; a project option's condition on a module dimension
/// holds when it holds for any module (docs/catalog-rules.md).
/// </summary>
public static class RuleEvaluator
{
    /// <summary>Checks a freshly resolved choice set, treating the module defaults as the only module.</summary>
    public static IReadOnlyList<string> Check(CatalogModel catalog, ResolvedChoices choices)
    {
        ArgumentNullException.ThrowIfNull(choices);

        return Check(catalog, choices.Project, [("module defaults", choices.ModuleDefaults)]);
    }

    public static IReadOnlyList<string> Check(
        CatalogModel catalog,
        IReadOnlyDictionary<string, IReadOnlyList<string>> project,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        return Check(catalog, project, [.. modules.Select(module => ($"module '{module.Key}'", module.Value))]);
    }

    private static List<string> Check(
        CatalogModel catalog,
        IReadOnlyDictionary<string, IReadOnlyList<string>> project,
        IReadOnlyList<(string Label, IReadOnlyDictionary<string, IReadOnlyList<string>> Values)> modules)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(project);

        var moduleViews = modules
            .Select(module => (module.Label, View: (IReadOnlyDictionary<string, IReadOnlyList<string>>)project.Concat(module.Values).ToDictionary()))
            .ToList();

        List<string> violations = [];

        var projectContext = new Context(
            Prefix: string.Empty,
            Holds: condition => IsModuleScoped(catalog, condition) && moduleViews.Count > 0
                ? moduleViews.Any(module => ConditionEvaluator.Holds(condition, module.View))
                : ConditionEvaluator.Holds(condition, project),
            DescribeActual: condition => IsModuleScoped(catalog, condition) && moduleViews.Count > 0
                ? "no module satisfies it"
                : ConditionEvaluator.DescribeActual(condition, project));
        CheckScope(catalog, DimensionScope.Project, project, projectContext, violations);

        foreach (var (label, view) in moduleViews)
        {
            var moduleContext = new Context(
                Prefix: $"{label}: ",
                Holds: condition => ConditionEvaluator.Holds(condition, view),
                DescribeActual: condition => ConditionEvaluator.DescribeActual(condition, view));
            CheckScope(catalog, DimensionScope.Module, view, moduleContext, violations);
        }

        return violations;
    }

    private static void CheckScope(
        CatalogModel catalog,
        DimensionScope scope,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        Context context,
        List<string> violations)
    {
        foreach (var dimension in catalog.Dimensions.Values.Where(dimension => dimension.Scope == scope))
        {
            if (!values.TryGetValue(dimension.Id, out var chosen))
            {
                continue;
            }

            foreach (var option in chosen.Select(id => dimension.Options[id]))
            {
                var subject = $"{context.Prefix}{dimension.Id}={option.Id}";
                violations.AddRange(option.Requires
                    .Where(condition => !context.Holds(condition))
                    .Select(condition => $"{subject} requires {condition}, but {context.DescribeActual(condition)}."));
                violations.AddRange(option.Conflicts
                    .Where(context.Holds)
                    .Select(condition => $"{subject} conflicts with {condition}."));
            }
        }
    }

    private static bool IsModuleScoped(CatalogModel catalog, Condition condition) =>
        catalog.Dimensions.TryGetValue(condition.Dimension, out var dimension) && dimension.Scope == DimensionScope.Module;

    private sealed record Context(string Prefix, Func<Condition, bool> Holds, Func<Condition, string> DescribeActual);
}

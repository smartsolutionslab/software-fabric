namespace Fabric.Core.Choices;

/// <summary>
/// Everything a generated project has chosen: project-scoped values, the module defaults new modules start from,
/// and each module's own values. This is what <c>fabric.yaml</c> records.
/// </summary>
public sealed record ProjectChoices(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Project,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ModuleDefaults,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Modules);

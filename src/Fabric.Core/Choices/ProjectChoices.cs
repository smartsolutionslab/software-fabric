namespace Fabric.Core.Choices;

/// <summary>
/// Everything a generated project has chosen: project-scoped values, the module defaults new modules start from,
/// and each module's own values. This is what <c>fabric.yaml</c> records.
/// </summary>
public sealed record ProjectChoices(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Project,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ModuleDefaults,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Modules)
{
    /// <summary>The chosen options' settings, keyed <c>&lt;dimension&gt;.&lt;option&gt;.&lt;setting&gt;</c>; empty when no chosen option declares one.</summary>
    public IReadOnlyDictionary<string, decimal> Settings { get; init; } = new Dictionary<string, decimal>();
}

namespace Fabric.Core.Choices;

/// <summary>
/// A complete choice set. <see cref="Project"/> holds project-scoped dimensions; <see cref="ModuleDefaults"/>
/// holds the module-scoped values every new module starts from. Both follow catalog order and contain only
/// active dimensions.
/// </summary>
public sealed record ResolvedChoices(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Project,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ModuleDefaults)
{
    /// <summary>Every chosen option's settings, set or at their defaults, keyed <c>&lt;dimension&gt;.&lt;option&gt;.&lt;setting&gt;</c>, in catalog order.</summary>
    public IReadOnlyDictionary<string, decimal> Settings { get; init; } = new Dictionary<string, decimal>();
}

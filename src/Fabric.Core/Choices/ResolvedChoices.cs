namespace Fabric.Core.Choices;

/// <summary>
/// A complete choice set. <see cref="Project"/> holds project-scoped dimensions; <see cref="ModuleDefaults"/>
/// holds the module-scoped values every new module starts from. Both follow catalog order and contain only
/// active dimensions.
/// </summary>
public sealed record ResolvedChoices(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Project,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ModuleDefaults);

/// <summary>Either <see cref="Choices"/> or at least one entry in <see cref="Errors"/>.</summary>
public sealed record ChoiceResolution(ResolvedChoices? Choices, IReadOnlyList<string> Errors);

/// <summary>Either <see cref="Values"/> for one module or at least one entry in <see cref="Errors"/>.</summary>
public sealed record ModuleResolution(IReadOnlyDictionary<string, IReadOnlyList<string>>? Values, IReadOnlyList<string> Errors);

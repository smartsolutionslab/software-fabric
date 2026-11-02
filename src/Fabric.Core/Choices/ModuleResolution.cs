namespace Fabric.Core.Choices;

/// <summary>Either <see cref="Values"/> for one module or at least one entry in <see cref="Errors"/>.</summary>
public sealed record ModuleResolution(IReadOnlyDictionary<string, IReadOnlyList<string>>? Values, IReadOnlyList<string> Errors);

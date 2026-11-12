namespace Fabric.Core.Generation;

/// <summary>One <c>add-module</c> call of a <see cref="Regeneration"/>: the module's name and every value it records.</summary>
public sealed record RegeneratedModule(string Name, IReadOnlyList<string> Overrides);

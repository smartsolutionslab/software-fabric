namespace Fabric.Core.Packs;

/// <summary>Packs keyed by option id in ordinal order, plus every problem found while loading.</summary>
public sealed record PackLoadResult(IReadOnlyDictionary<string, PackManifest> Packs, IReadOnlyList<string> Errors);

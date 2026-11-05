namespace Fabric.Core.Packs;

/// <summary>A loaded <c>pack.yaml</c> (schema/pack.schema.json). Paths are relative to <see cref="Directory"/>.</summary>
public sealed record PackManifest(
    string Option,
    string Directory,
    IReadOnlyList<PackFile> Files,
    IReadOnlyList<SlotContribution> Slots,
    IReadOnlyList<SlotDeclaration> DeclaredSlots,
    PackAgents Agents,
    string Adr,
    IReadOnlyList<PackGate> Gates)
{
    /// <summary>Packs of this dimension are always part of a generated project and have no catalog entry.</summary>
    public const string BaseDimension = "base";

    public string Dimension => Option[..Option.LastIndexOf('.')];

    public string OptionName => Option[(Option.LastIndexOf('.') + 1)..];

    public bool IsBase => Dimension == BaseDimension;

    /// <summary>
    /// Modules this pack brings, each with its pinned module-scoped choices (ADR-0005). <c>fabric new</c> adds them as
    /// <c>add-module</c> would; only a base or project-scoped pack may bring one.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Modules { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>();
}

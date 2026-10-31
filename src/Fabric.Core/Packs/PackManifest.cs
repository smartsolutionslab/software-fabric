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
}

public sealed record PackFile(string Template, string Target, bool PerModule);

public sealed record SlotContribution(string Slot, string Template, int Order);

public enum SlotCardinality
{
    ZeroOrMore,
    ExactlyOne,
    AtMostOne,
}

/// <param name="Separator">Joins the slot's contributions; <c>null</c> means a newline.</param>
public sealed record SlotDeclaration(string Slot, string Description, SlotCardinality Cardinality, string? Separator = null);

public sealed record PackAgents(string AgentsMd, IReadOnlyDictionary<string, string> Briefs);

public enum GateKind
{
    ArchitectureTest,
    Analyzer,
    LintRule,
    CiStep,
    UnitTest,
    EndToEndTest,
}

public sealed record PackGate(GateKind Kind, string Describes, string? File);

/// <summary>Packs keyed by option id in ordinal order, plus every problem found while loading.</summary>
public sealed record PackLoadResult(IReadOnlyDictionary<string, PackManifest> Packs, IReadOnlyList<string> Errors);

namespace Fabric.Core.Packs;

/// <param name="Module">The module this file is rendered for; <c>null</c> for project files.</param>
public sealed record ComposedFile(PackManifest Pack, PackFile File, string Target, string? Module);

public sealed record SlotEntry(PackManifest Pack, SlotContribution Contribution);

/// <summary>The selected packs, the files they produce and the ordered contents of every slot, or errors.</summary>
public sealed record Composition(
    IReadOnlyList<PackManifest> Packs,
    IReadOnlyList<ComposedFile> Files,
    IReadOnlyDictionary<string, IReadOnlyList<SlotEntry>> Slots,
    IReadOnlyList<string> Errors);

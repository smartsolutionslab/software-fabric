namespace Fabric.Core.Packs;

/// <summary>The selected packs, the files they produce and the ordered contents of every slot, or errors.</summary>
/// <param name="SlotSeparators">Separators of the slots that declare one; any other slot joins with a newline.</param>
public sealed record Composition(
    IReadOnlyList<PackManifest> Packs,
    IReadOnlyList<ComposedFile> Files,
    IReadOnlyDictionary<string, IReadOnlyList<SlotEntry>> Slots,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string>? SlotSeparators = null);

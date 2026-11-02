namespace Fabric.Core.Packs;

/// <param name="Separator">Joins the slot's contributions; <c>null</c> means a newline.</param>
public sealed record SlotDeclaration(string Slot, string Description, SlotCardinality Cardinality, string? Separator = null);

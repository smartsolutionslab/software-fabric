namespace Fabric.Core.Packs;

/// <param name="Optional">Dropped when no selected pack declares the slot, instead of being a generation error.</param>
public sealed record SlotContribution(string Slot, string Template, int Order, bool Optional = false);

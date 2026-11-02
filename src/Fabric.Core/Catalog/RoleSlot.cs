namespace Fabric.Core.Catalog;

/// <param name="Separator">Joins the slot's contributions; <c>null</c> means a newline. Every option's declaration must use it.</param>
public sealed record RoleSlot(string Slot, string Description, string? Separator);

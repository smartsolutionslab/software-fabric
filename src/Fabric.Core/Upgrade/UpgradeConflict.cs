namespace Fabric.Core.Upgrade;

public sealed record UpgradeConflict(string Path, ConflictKind Kind);

namespace Fabric.Core.Upgrade;

/// <summary>A path the upgrade staged, with git's status letter against the project's HEAD: A, M or D.</summary>
public sealed record ChangedFile(char Status, string Path);

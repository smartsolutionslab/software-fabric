namespace Fabric.Core.Packs;

/// <param name="Module">The module this file is rendered for; <c>null</c> for project files.</param>
public sealed record ComposedFile(PackManifest Pack, PackFile File, string Target, string? Module);

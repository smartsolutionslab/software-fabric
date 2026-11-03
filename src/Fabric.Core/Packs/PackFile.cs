namespace Fabric.Core.Packs;

/// <param name="When">A Scriban condition; the file is written only when it evaluates to <c>true</c>. <c>null</c> always writes it.</param>
public sealed record PackFile(string Template, string Target, bool PerModule, string? When = null);

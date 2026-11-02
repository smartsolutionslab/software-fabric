namespace Fabric.Core.Packs;

public sealed record PackAgents(string AgentsMd, IReadOnlyDictionary<string, string> Briefs);

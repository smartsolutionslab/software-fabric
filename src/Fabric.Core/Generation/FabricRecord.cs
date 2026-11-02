using Fabric.Core.Choices;

namespace Fabric.Core.Generation;

public sealed record FabricRecord(string Name, string? Profile, ProjectChoices Choices);

namespace Fabric.Core.Choices;

/// <summary>Either <see cref="Choices"/> or at least one entry in <see cref="Errors"/>.</summary>
public sealed record ChoiceResolution(ResolvedChoices? Choices, IReadOnlyList<string> Errors);

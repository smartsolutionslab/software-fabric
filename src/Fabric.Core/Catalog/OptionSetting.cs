namespace Fabric.Core.Catalog;

/// <summary>
/// A number an option needs besides being chosen (the evals threshold, ADR-0013). Recorded in <c>fabric.yaml</c>
/// under <c>settings</c> as <c>&lt;dimension&gt;.&lt;option&gt;.&lt;setting&gt;</c>, between <see cref="Minimum"/> and <see cref="Maximum"/>.
/// </summary>
public sealed record OptionSetting(string Id, decimal Default, decimal Minimum, decimal Maximum, string? Note);

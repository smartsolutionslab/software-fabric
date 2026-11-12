using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Generation;

/// <summary>
/// The <c>fabric new</c> and <c>add-module</c> calls that write a project again from its <c>fabric.yaml</c> alone
/// (ADR-0016 E4): its profile, every recorded choice and setting spelled out as an override, so the result does not
/// depend on the profile's or the catalog's defaults, and then each module with all its values, in the order recorded.
/// Only flags every released fabric understands, so an old generator can run it too. A module a pack brings is
/// written by <c>new</c> and must be skipped when it is already there.
/// </summary>
public sealed record Regeneration(string Name, string? Profile, IReadOnlyList<string> Overrides, IReadOnlyList<RegeneratedModule> Modules)
{
    public static Regeneration From(FabricRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var choices = record.Choices;
        List<string> overrides = [
            .. Assignments(choices.Project),
            .. choices.Settings.Select(setting => $"{setting.Key}={SettingNumber.Format(setting.Value)}"),
            .. Assignments(choices.ModuleDefaults),
        ];

        return new Regeneration(
            record.Name,
            record.Profile,
            overrides,
            [.. choices.Modules.Select(module => new RegeneratedModule(module.Key, Assignments(module.Value)))]);
    }

    private static List<string> Assignments(IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        [.. values.Select(value => $"{value.Key}={string.Join(',', value.Value)}")];
}

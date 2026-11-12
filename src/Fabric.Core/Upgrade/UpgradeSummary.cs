using System.Globalization;
using System.Text;

namespace Fabric.Core.Upgrade;

/// <summary>
/// What an upgrade prints (ADR-0016 E4, D1): the agent's brief for finishing it. Which files were taken from the new
/// generator, which were merged with the project's edits, which conflict, and what D1 does not guarantee where it
/// applies. Deterministic: paths in order, nothing from the machine.
/// </summary>
public static class UpgradeSummary
{
    public static string Write(string name, string oldVersion, string newVersion, UpgradeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var text = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"upgraded {name} from fabric {oldVersion} to fabric {newVersion}; nothing is committed.");
        if (outcome.Taken.Count + outcome.Merged.Count + outcome.Conflicts.Count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"nothing changed: the project already has what fabric {newVersion} writes.");
        }

        Section(text, $"taken from fabric {newVersion}", outcome.Taken.Select(file => $"{file.Status} {file.Path}").ToList());
        Section(text, "merged with the project's edits", outcome.Merged.Select(file => $"{file.Status} {file.Path}").ToList());
        Section(
            text,
            "conflicts to resolve, then git add and commit",
            outcome.Conflicts.Select(conflict => $"{Describe(conflict.Kind, newVersion)}: {conflict.Path}").ToList());
        var notes = NotGuaranteed(outcome);
        if (notes.Count > 0)
        {
            text.AppendLine("not guaranteed (ADR-0016 D1):");
            notes.ForEach(note => text.AppendLine(CultureInfo.InvariantCulture, $"  {note}"));
        }

        return text.ToString();
    }

    private static void Section(StringBuilder text, string heading, List<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"{heading} ({lines.Count}):");
        foreach (var line in lines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {line}");
        }
    }

    private static string Describe(ConflictKind kind, string newVersion) => kind switch
    {
        ConflictKind.BothChanged => "both changed",
        ConflictKind.BothAdded => "both added",
        ConflictKind.DeletedByProject => $"deleted by the project, changed by fabric {newVersion}",
        ConflictKind.DeletedByGenerator => $"changed by the project, deleted by fabric {newVersion}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>D1's list, each only where the upgrade touched what it is about.</summary>
    private static List<string> NotGuaranteed(UpgradeOutcome outcome)
    {
        var paths = outcome.Taken.Select(file => file.Path)
            .Concat(outcome.Merged.Select(file => file.Path))
            .Concat(outcome.Conflicts.Select(conflict => conflict.Path))
            .ToList();

        List<string> notes = [];
        if (outcome.ProjectEdited)
        {
            notes.Add("- that the project builds and passes its tests: it has edits of its own; build and test it.");
        }

        if (paths.Any(path => System.IO.Path.GetFileName(path) == "package.json"))
        {
            notes.Add("- that pnpm-lock.yaml matches a changed package.json: install again and commit the lockfile.");
        }

        if (paths.Any(path => path.EndsWith(".cs", StringComparison.Ordinal)))
        {
            notes.Add("- that a changed persistence model has its EF Core migration: C# files changed; add a migration if a model did.");
        }

        if (paths.Any(path => path.StartsWith("docs/adr/", StringComparison.Ordinal)))
        {
            notes.Add("- that the project's own ADRs and code agree with a changed pack ADR under docs/adr/.");
        }

        return notes;
    }
}

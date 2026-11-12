namespace Fabric.Core.Upgrade;

/// <summary>
/// What the merge left (ADR-0016 D8): paths taken from the new generator as they are, paths merged cleanly with the
/// project's edits, and conflicts, all ordered by path. <see cref="ProjectEdited"/> says whether the project differs
/// from what its recorded generator writes, edits and files of its own included.
/// </summary>
public sealed record UpgradeOutcome(
    IReadOnlyList<ChangedFile> Taken,
    IReadOnlyList<ChangedFile> Merged,
    IReadOnlyList<UpgradeConflict> Conflicts,
    bool ProjectEdited);

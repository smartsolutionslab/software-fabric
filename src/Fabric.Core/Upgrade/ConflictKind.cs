namespace Fabric.Core.Upgrade;

/// <summary>How a path is left unmerged, from the stages git holds for it: 1 base, 2 the project, 3 the new generator.</summary>
public enum ConflictKind
{
    /// <summary>Stages 1, 2 and 3: both sides changed it and the changes overlap. The file holds conflict markers.</summary>
    BothChanged,

    /// <summary>Stages 2 and 3: the project has a file where the new generator writes one. The file holds conflict markers.</summary>
    BothAdded,

    /// <summary>Stages 1 and 3: the project deleted a file the new generator changes.</summary>
    DeletedByProject,

    /// <summary>Stages 1 and 2: the project changed a file the new generator no longer writes.</summary>
    DeletedByGenerator,
}

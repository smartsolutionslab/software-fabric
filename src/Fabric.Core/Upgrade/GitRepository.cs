namespace Fabric.Core.Upgrade;

/// <summary>
/// The project's git repository, and the three-way merge of ADR-0016 D8: base and theirs are written as tree objects
/// that no commit refers to, and merged into the working tree and index with base named as the merge base. Nothing is
/// committed, and nothing but the merge's own objects is added to the repository.
/// </summary>
internal sealed class GitRepository
{
    private readonly string top;

    private readonly string gitDirectory;

    private GitRepository(string top, string gitDirectory)
    {
        this.top = top;
        this.gitDirectory = gitDirectory;
    }

    /// <summary>The repository whose top is <paramref name="directory"/>, with a commit and a clean working tree, or why not.</summary>
    public static (GitRepository? Repository, string? Error) Open(string directory)
    {
        var top = Path.GetFullPath(directory);
        if (!Directory.Exists(top))
        {
            return (null, $"{directory} does not exist.");
        }

        var prefix = Git(top, "rev-parse", "--show-prefix");
        if (prefix.ExitCode != 0 || prefix.Output.Trim().Length > 0)
        {
            return (null, $"{directory} is not the top of a git repository; fabric upgrade leaves its changes and conflicts in one (ADR-0016 D8).");
        }

        if (Git(top, "rev-parse", "--verify", "--quiet", "HEAD").ExitCode != 0)
        {
            return (null, $"{directory} has no commit yet; commit the project first, so the upgrade is the only change.");
        }

        var status = Git(top, "status", "--porcelain", "--untracked-files=all");
        if (status.ExitCode != 0 || status.Output.Length > 0)
        {
            return (null, $"{directory} has uncommitted changes; commit or stash them first, so the upgrade is the only change (ADR-0016 D8).");
        }

        return (new GitRepository(top, Git(top, "rev-parse", "--absolute-git-dir").Output.Trim()), null);
    }

    /// <summary>
    /// Writes every file under <paramref name="directory"/> into the repository as a tree object, through a scratch index,
    /// with the repository's own attributes and settings, as committing those files to it would.
    /// </summary>
    public string WriteTree(string directory, string indexFile)
    {
        var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = indexFile };
        Required(Git(directory, environment, $"--git-dir={gitDirectory}", $"--work-tree={directory}", "add", "--all", "--force", "--", "."));
        var tree = Required(Git(directory, environment, $"--git-dir={gitDirectory}", "write-tree")).Trim();
        File.Delete(indexFile);
        return tree;
    }

    /// <summary>
    /// Merges <paramref name="theirs"/> into HEAD with <paramref name="mergeBase"/> as the merge base. Paths one side
    /// changed are resolved by <c>read-tree --aggressive</c>; paths both changed, or both added, are merged with
    /// <c>git merge-file</c>, labelled as <paramref name="labels"/> says, and left unmerged where the edits overlap.
    /// </summary>
    public UpgradeOutcome Merge(string mergeBase, string theirs, (string Project, string Base, string Theirs) labels, string scratch)
    {
        Required(Git(top, "read-tree", "-m", "-u", "--aggressive", mergeBase, "HEAD", theirs));

        List<ChangedFile> merged = [];
        List<UpgradeConflict> conflicts = [];
        var (unmerged, notRegularFiles) = Unmerged();
        foreach (var (path, stages) in unmerged)
        {
            var kind = stages switch
            {
                [1, 2, 3] => ConflictKind.BothChanged,
                [2, 3] => ConflictKind.BothAdded,
                [1, 3] => ConflictKind.DeletedByProject,
                _ => ConflictKind.DeletedByGenerator,
            };

            // A symlink or submodule on any side is a type change, never merged as text: writing the result through a
            // link would change whatever it points at.
            if (kind is ConflictKind.BothChanged or ConflictKind.BothAdded && !notRegularFiles.Contains(path) &&
                MergeFile(path, stages.Contains(1), labels, scratch))
            {
                merged.Add(new ChangedFile(kind == ConflictKind.BothAdded ? 'A' : 'M', path));
            }
            else
            {
                conflicts.Add(new UpgradeConflict(path, kind));
            }
        }

        var mergedPaths = merged.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var staged = Required(Git(top, "diff", "--cached", "--name-status", "--no-renames", "-z", "HEAD")).Split('\0');
        List<ChangedFile> taken = [];
        for (var index = 0; index + 1 < staged.Length; index += 2)
        {
            if (staged[index] != "U" && !mergedPaths.Contains(staged[index + 1]))
            {
                taken.Add(new ChangedFile(staged[index][0], staged[index + 1]));
            }
        }

        var edited = Git(top, "diff", "--quiet", mergeBase, "HEAD").ExitCode != 0;
        return new UpgradeOutcome(
            [.. taken.OrderBy(file => file.Path, StringComparer.Ordinal)],
            [.. merged.OrderBy(file => file.Path, StringComparer.Ordinal)],
            [.. conflicts.OrderBy(conflict => conflict.Path, StringComparer.Ordinal)],
            edited);
    }

    /// <summary>
    /// Every unmerged path, ordered, with the stages git holds for it, and the paths where a stage is anything but a
    /// regular file (a symlink, a submodule).
    /// </summary>
    private (SortedDictionary<string, List<int>> Paths, HashSet<string> NotRegularFiles) Unmerged()
    {
        SortedDictionary<string, List<int>> paths = new(StringComparer.Ordinal);
        HashSet<string> notRegularFiles = new(StringComparer.Ordinal);
        foreach (var entry in Required(Git(top, "ls-files", "--unmerged", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var (info, path) = (entry[..entry.IndexOf('\t', StringComparison.Ordinal)], entry[(entry.IndexOf('\t', StringComparison.Ordinal) + 1)..]);
            var fields = info.Split(' ');
            var stage = fields[2][0] - '0';
            if (fields[0] is not ("100644" or "100755"))
            {
                notRegularFiles.Add(path);
            }

            if (!paths.TryGetValue(path, out var stages))
            {
                paths.Add(path, stages = []);
            }

            stages.Add(stage);
        }

        foreach (var stages in paths.Values)
        {
            stages.Sort();
        }

        return (paths, notRegularFiles);
    }

    /// <summary>
    /// Merges one path's stages with <c>git merge-file</c>, an empty file standing in for a missing base, and writes the
    /// result, conflict markers included, over the project's file. A clean result is staged.
    /// </summary>
    /// <returns>Whether it merged without a conflict.</returns>
    private bool MergeFile(string path, bool hasBase, (string Project, string Base, string Theirs) labels, string scratch)
    {
        Directory.CreateDirectory(scratch);
        var (project, mergeBase, theirs) = (Path.Combine(scratch, "project"), Path.Combine(scratch, "base"), Path.Combine(scratch, "theirs"));
        Required(GitToFile(top, project, "cat-file", "blob", $":2:{path}"));
        Required(GitToFile(top, theirs, "cat-file", "blob", $":3:{path}"));
        if (hasBase)
        {
            Required(GitToFile(top, mergeBase, "cat-file", "blob", $":1:{path}"));
        }
        else
        {
            File.WriteAllBytes(mergeBase, []);
        }

        var result = Git(scratch, "merge-file", "--diff3", "-L", labels.Project, "-L", labels.Base, "-L", labels.Theirs, project, mergeBase, theirs);
        if (result.ExitCode is < 0 or > 127)
        {
            return false;
        }

        var target = Path.Combine(top, path);
        if (new FileInfo(target).LinkTarget is not null)
        {
            return false;
        }

        File.WriteAllBytes(target, File.ReadAllBytes(project));
        if (result.ExitCode != 0)
        {
            return false;
        }

        Required(Git(top, "update-index", "--", path));
        return true;
    }

    private static ProcessResult Git(string directory, params string[] arguments) => Git(directory, null, arguments);

    private static ProcessResult Git(string directory, IReadOnlyDictionary<string, string>? environment, params string[] arguments) =>
        Processes.Run("git", arguments, directory, environment);

    private static ProcessResult GitToFile(string directory, string outputFile, params string[] arguments) =>
        Processes.Run("git", arguments, directory, outputFile: outputFile);

    private static string Required(ProcessResult result) =>
        result.ExitCode == 0 ? result.Output : throw new IOException($"git failed: {result.Printed}");
}

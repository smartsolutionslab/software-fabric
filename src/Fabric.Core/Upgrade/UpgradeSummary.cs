using System.Globalization;
using System.Text;
using Fabric.Core.Packs;

namespace Fabric.Core.Upgrade;

/// <summary>
/// What an upgrade prints (ADR-0016 E4, D1): the agent's brief for finishing it. Which files were taken from the new
/// generator, which were merged with the project's edits, which conflict, and what D1 does not guarantee where it
/// applies. Deterministic: paths in order, nothing from the machine.
/// </summary>
public static class UpgradeSummary
{
    /// <summary>The committed copy of the host's OpenAPI document, written by every frontend api-client pack.</summary>
    public const string OpenApiDocument = "web/app/openapi.json";

    /// <summary>The module the client code a frontend api-client pack generates imports, written by every one of them.</summary>
    public const string BaseApi = "web/app/src/api/baseApi.ts";

    private const string WebSource = "web/app/src/";

    private const string AdrDirectory = "docs/adr/";

    /// <summary>The ef-core pack's rule that fails on a store registered by hand and by AddModuleAdapters (#845).</summary>
    private const string RegisteredOnceRule = "tests/Architecture.Tests/Rules/EveryModuleServiceIsRegisteredOnce.cs";

    /// <summary>
    /// Writes the summary. <paramref name="hasOpenApiDocument"/> and <paramref name="hasBaseApi"/> say whether the
    /// project's choices write <see cref="OpenApiDocument"/> and <see cref="BaseApi"/>, so the summary names each only
    /// for a project that has it. <paramref name="skippedProbeTests"/> are the probe tests the upgrade left out beside a
    /// page the project rewrote (#676).
    /// </summary>
    public static string Write(
        string name,
        string oldVersion,
        string newVersion,
        UpgradeOutcome outcome,
        bool hasOpenApiDocument,
        bool hasBaseApi,
        IReadOnlyList<string> skippedProbeTests)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(skippedProbeTests);

        return Write(
            $"upgraded {name} from fabric {oldVersion} to fabric {newVersion}; nothing is committed.",
            $"fabric {newVersion}",
            outcome,
            hasOpenApiDocument,
            hasBaseApi,
            [.. skippedProbeTests.Select(path =>
                $"- that {path} is added: the project's page beside it differs from what fabric {oldVersion} wrote, and the test asserts " +
                "the generated page; generate it with fabric and add it by hand if the page keeps a Manage section.")]);
    }

    /// <summary>
    /// Writes the summary of <c>fabric change</c> (ADR-0016 C1): the same sections as an upgrade's, with the changed
    /// choices, <c>dimension=option</c>, standing where an upgrade names the new generator.
    /// </summary>
    public static string WriteChange(
        string name,
        IReadOnlyList<ChoiceChange> changes,
        UpgradeOutcome outcome,
        bool hasOpenApiDocument,
        bool hasBaseApi)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(outcome);

        var described = string.Join(", ", changes.Select(change => $"{change.Dimension} from {change.From} to {change.To}"));
        return Write(
            $"changed {name}'s {described}; nothing is committed.",
            string.Join(' ', changes.Select(change => $"{change.Dimension}={change.To}")),
            outcome,
            hasOpenApiDocument,
            hasBaseApi,
            []);
    }

    /// <summary>
    /// Writes the summary of <c>add-module</c> for a module that selects packs new to the project (ADR-0005, amended
    /// 2026-11-20): the same sections as an upgrade's, with <c>add-module &lt;module&gt;</c> standing where an upgrade names
    /// the new generator.
    /// </summary>
    public static string WriteModule(
        string name,
        string module,
        IReadOnlyList<string> newPacks,
        UpgradeOutcome outcome,
        bool hasOpenApiDocument,
        bool hasBaseApi)
    {
        ArgumentNullException.ThrowIfNull(newPacks);
        ArgumentNullException.ThrowIfNull(outcome);

        return Write(
            $"added module {module} to {name}, merging in what its packs new to the project write ({string.Join(", ", newPacks)}); nothing is committed.",
            $"add-module {module}",
            outcome,
            hasOpenApiDocument,
            hasBaseApi,
            []);
    }

    /// <param name="theirs">What wrote the merged-in tree, as the sections name it.</param>
    private static string Write(
        string headline,
        string theirs,
        UpgradeOutcome outcome,
        bool hasOpenApiDocument,
        bool hasBaseApi,
        IReadOnlyList<string> extraNotes)
    {
        var text = new StringBuilder().AppendLine(headline);
        if (outcome.Taken.Count + outcome.Merged.Count + outcome.Conflicts.Count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"nothing changed: the project already has what {theirs} writes.");
        }

        Section(text, $"taken from {theirs}", outcome.Taken.Select(file => $"{file.Status} {file.Path}").ToList());
        Section(text, "merged with the project's edits", outcome.Merged.Select(file => $"{file.Status} {file.Path}").ToList());
        Section(
            text,
            "conflicts to resolve, then git add and commit",
            outcome.Conflicts.Select(conflict => $"{Describe(conflict.Kind, theirs)}: {conflict.Path}").ToList());
        var moves = MovedPackAdrs(outcome);
        Section(text, $"pack ADRs moved under {Composer.PackAdrDirectory}", moves.Select(move => $"{move.From} -> {move.To}").ToList());
        var notes = NotGuaranteed(outcome, hasOpenApiDocument, hasBaseApi);
        notes.AddRange(MoveNotes(moves));
        notes.AddRange(extraNotes);
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

    private static string Describe(ConflictKind kind, string theirs) => kind switch
    {
        ConflictKind.BothChanged => "both changed",
        ConflictKind.BothAdded => "both added",
        ConflictKind.DeletedByProject => $"deleted by the project, changed by {theirs}",
        ConflictKind.DeletedByGenerator => $"changed by the project, deleted by {theirs}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The pack ADRs fabric 0.7.0 and earlier numbered by their place among the selected packs,
    /// <c>docs/adr/NNNN-&lt;dimension&gt;-&lt;option&gt;.md</c>, that this merge removes while it adds the same name under
    /// <see cref="Composer.PackAdrDirectory"/> (ADR-0018). <c>Edited</c>: the project had changed the old file, so its
    /// removal is a conflict.
    /// </summary>
    private static List<(string From, string To, bool Edited)> MovedPackAdrs(UpgradeOutcome outcome)
    {
        var added = outcome.Taken.Where(file => file.Status == 'A').Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        return [.. outcome.Taken.Where(file => file.Status == 'D').Select(file => (file.Path, Edited: false))
            .Concat(outcome.Conflicts.Where(conflict => conflict.Kind == ConflictKind.DeletedByGenerator).Select(conflict => (conflict.Path, Edited: true)))
            .Select(removed => (From: removed.Path, To: Composer.PackAdrDirectory + UnnumberedName(removed.Path), removed.Edited))
            .Where(move => move.To != Composer.PackAdrDirectory && added.Contains(move.To))
            .OrderBy(move => move.From, StringComparer.Ordinal)];
    }

    /// <summary><c>base-core.md</c> for <c>docs/adr/0001-base-core.md</c>; empty for any other path.</summary>
    private static string UnnumberedName(string path)
    {
        if (!path.StartsWith(AdrDirectory, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var name = path[AdrDirectory.Length..];
        return name.Length > 5 && !name.Contains('/', StringComparison.Ordinal) && name[..4].All(char.IsAsciiDigit) && name[4] == '-'
            ? name[5..]
            : string.Empty;
    }

    private static IEnumerable<string> MoveNotes(List<(string From, string To, bool Edited)> moves)
    {
        if (moves.Count == 0)
        {
            yield break;
        }

        yield return "- that the project's own ADRs, docs and code cite the moved pack ADRs by their new names: search for each old file name " +
            $"and its number (ADR-NNNN) and point them at the new name under {Composer.PackAdrDirectory}.";
        foreach (var move in moves.Where(move => move.Edited))
        {
            yield return $"- that the project's edits of a moved pack ADR reach its new name: carry them from {move.From} to {move.To}, then git rm the old file.";
        }
    }

    /// <summary>D1's list, each only where the upgrade touched what it is about.</summary>
    private static List<string> NotGuaranteed(UpgradeOutcome outcome, bool hasOpenApiDocument, bool hasBaseApi)
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

        // The host serves the document from its C#, all of which is under src/; which change alters it cannot be told
        // from a path (a Kernel enum's JSON converter is enough), so any C# file there counts.
        if (hasOpenApiDocument && paths.Any(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)))
        {
            notes.Add($"- that {OpenApiDocument} and the client generated from it match what the host serves: C# under src/ changed; " +
                "run the integration tests, which rewrite the document, commit it, then run pnpm codegen in web/app and adapt the screens.");
        }

        // Client code a pack generates is written against the stock base API (its exports, and with RTK Query the
        // FetchBaseQueryError its base query reports); a project's own wrapper may not give them. A change to the base
        // API alone brings no new code that relies on it, and is listed as taken, merged or a conflict anyway.
        if (hasBaseApi && paths.Any(path => path.StartsWith(WebSource, StringComparison.Ordinal) && path != BaseApi))
        {
            notes.Add($"- that generated client code under {WebSource} works with the project's {BaseApi}: it relies on that file's " +
                "exports and its base query's error type as fabric writes them (FetchBaseQueryError with RTK Query); keep them, then run pnpm build in web/app.");
        }

        // A project generated before AddModuleAdapters (0.8.0) registers its stores by hand. Which registrations are
        // its own cannot be told from the generated trees, so the rule names them and the summary only says so.
        if (paths.Contains(RegisteredOnceRule, StringComparer.Ordinal))
        {
            notes.Add("- that no module registers by hand a store AddModuleAdapters registers by convention: the architecture rule " +
                "EveryModuleServiceIsRegisteredOnce fails on such a second registration, naming it; delete the hand registration.");
        }

        if (paths.Any(path => path.StartsWith("docs/adr/", StringComparison.Ordinal)))
        {
            notes.Add("- that the project's own ADRs and code agree with a changed pack ADR under docs/adr/.");
        }

        return notes;
    }
}

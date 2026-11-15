using System.Text.RegularExpressions;
using Fabric.Core.Rendering;

namespace Fabric.Core.Upgrade;

/// <summary>
/// The probe tests an upgrade does not add (#676): a new <c>&lt;Name&gt;Page.probe.test.*</c> asserts the page fabric
/// writes beside it (its Manage section) and renders it without the app's store, so beside a page the project rewrote
/// it fails. The upgrade leaves it out there and the summary names it (ADR-0016 D1).
/// </summary>
internal static partial class ProbeTests
{
    /// <summary>
    /// The probe tests in <paramref name="theirs"/> that neither the base nor the project has, beside a module page that
    /// differs between <paramref name="baseTree"/>, what the recorded generator wrote, and the project's HEAD, ordered.
    /// </summary>
    public static IReadOnlyList<string> Skipped(IReadOnlyList<RenderedFile> theirs, GitRepository repository, string baseTree)
    {
        List<string> skipped = [];
        foreach (var file in theirs)
        {
            var match = Probe().Match(file.Path);
            if (!match.Success || repository.Has(baseTree, file.Path) || repository.Has("HEAD", file.Path))
            {
                continue;
            }

            var (directory, page) = (match.Groups["directory"].Value, match.Groups["page"].Value);
            if (repository.ChangedSince(baseTree, directory).Any(path => IsPage(path, directory, page)))
            {
                skipped.Add(file.Path);
            }
        }

        return [.. skipped.Order(StringComparer.Ordinal)];
    }

    /// <summary>The page itself, <c>&lt;Name&gt;Page.&lt;extension&gt;</c>, not another file named after it such as its tests.</summary>
    private static bool IsPage(string path, string directory, string page) =>
        path.StartsWith(directory + page + ".", StringComparison.Ordinal) && !path[(directory.Length + page.Length + 1)..].Contains('.', StringComparison.Ordinal);

    [GeneratedRegex(@"^(?<directory>web/app/src/modules/[^/]+/)(?<page>[^/.]+Page)\.probe\.test\.[^/.]+$")]
    private static partial Regex Probe();
}

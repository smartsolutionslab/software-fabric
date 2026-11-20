namespace Fabric.Cli.Tests;

/// <summary>
/// Stands in for a released fabric, which a test cannot install: a script that reports <c>version</c>, runs this build's
/// CLI with the given catalog and packs, and records <c>version</c> in the <c>fabric.yaml</c> it writes, as that release
/// would. What it generates is what this build generates from those packs, except that with <c>numberedAdrs</c> it names
/// the pack ADRs <c>docs/adr/NNNN-&lt;dimension&gt;-&lt;option&gt;.md</c>, as fabric 0.7.0 and earlier did (#840).
/// </summary>
/// <remarks>
/// The script is <c>old-fabric.sh</c>, which the build copies beside the tests; a test gets a link to it and a settings
/// file, never a script of its own. Linux refuses to start a file any process holds open for writing (ETXTBSY), and a
/// process forked by a test running in parallel holds every descriptor this process had open at that moment until it
/// calls exec, so a script the tests had just written could not be started safely.
/// </remarks>
internal static class OldFabric
{
    public static string Create(string directory, string version, string catalog, string packs, bool numberedAdrs = false)
    {
        var script = Path.Combine(directory, $"fabric-{version}");
        File.WriteAllText($"{script}.settings", $"{version}\n{catalog}\n{packs}\n{(numberedAdrs ? "numbered-adrs" : string.Empty)}\n");
        File.CreateSymbolicLink(script, Path.Combine(AppContext.BaseDirectory, "old-fabric.sh"));
        return script;
    }
}

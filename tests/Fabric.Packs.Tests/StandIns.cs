using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

/// <summary>
/// Empty packs for every option a profile chooses that has no real pack yet, so a profile can be generated and built
/// while its packs are written one task at a time (roadmap M1-02). A stand-in owns no files and fills no slots.
/// </summary>
internal sealed class StandIns : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("fabric-stand-ins-").FullName;

    public StandIns()
    {
        File.WriteAllText(Path.Combine(directory, "adr.md"), "# Stand-in\n\nThis option's pack is not written yet.\n");
        File.WriteAllText(Path.Combine(directory, "agents.md"), string.Empty);
    }

    public IReadOnlyList<string> StoodIn { get; private set; } = [];

    public IReadOnlyDictionary<string, PackManifest> Complete(CatalogModel catalog, IReadOnlyDictionary<string, PackManifest> packs, string profile)
    {
        var resolved = ChoiceResolver.Resolve(catalog, profile, []).Choices
            ?? throw new InvalidOperationException($"profile {profile} does not resolve.");

        var chosen = resolved.Project.Concat(resolved.ModuleDefaults)
            .SelectMany(entry => entry.Value.Select(option => $"{entry.Key}.{option}"))
            .ToList();

        StoodIn = [.. chosen.Where(option => !packs.ContainsKey(option))];
        var completed = packs.ToDictionary();
        foreach (var option in StoodIn)
        {
            completed[option] = new PackManifest(
                option,
                directory,
                [],
                [],
                [],
                new PackAgents("agents.md", new Dictionary<string, string>()),
                "adr.md",
                [new PackGate(GateKind.UnitTest, "Stand-in: nothing to check yet.", null)]);
        }

        return completed;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

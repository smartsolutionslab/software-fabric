using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

/// <summary>A pack for one option that only contributes the given templates to slots, for testing a slot's contract.</summary>
internal sealed class ProbePack : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("fabric-probe-").FullName;

    public ProbePack(string option, IReadOnlyDictionary<string, string> contributions)
    {
        File.WriteAllText(Path.Combine(directory, "adr.md"), "# Probe\n");
        File.WriteAllText(Path.Combine(directory, "agents.md"), string.Empty);

        List<SlotContribution> slots = [];
        foreach (var (slot, template) in contributions)
        {
            var file = slot + ".sbn";
            File.WriteAllText(Path.Combine(directory, file), template);
            slots.Add(new SlotContribution(slot, file, 0));
        }

        Manifest = new PackManifest(
            option,
            directory,
            [],
            slots,
            [],
            new PackAgents("agents.md", new Dictionary<string, string>()),
            "adr.md",
            [new PackGate(GateKind.UnitTest, "Probe: nothing to check.", null)]);
    }

    public PackManifest Manifest { get; }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

namespace Fabric.Core.Tests.Packs;

/// <summary>A throw-away packs directory. Each pack lives at &lt;root&gt;/&lt;dimension path&gt;/&lt;option&gt;/.</summary>
internal sealed class PackFixture : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("fabric-packs-").FullName;

    public static string DefaultManifest(string option) => $$"""
        option: {{option}}
        files:
          - { template: templates/readme.md.sbn, target: docs/readme.md, per-module: false }
        slots:
          - { slot: base.readme.sections, template: templates/section.md.sbn, order: 10 }
        agents:
          agents-md: docs/agents.md
          briefs: { backend-engineer: docs/engineer.md }
        adr: docs/adr.md
        gates:
          - { kind: unit-test, describes: "The pack renders.", file: templates/gate.cs.sbn }
        """;

    public static readonly string[] DefaultFiles =
        ["templates/readme.md.sbn", "templates/section.md.sbn", "docs/agents.md", "docs/engineer.md", "docs/adr.md", "templates/gate.cs.sbn"];

    public PackFixture AddDefault(string option) => Add(option, DefaultManifest(option), DefaultFiles);

    public PackFixture Add(string option, string manifest, params string[] files) =>
        AddAt(option.Split('.'), manifest, files);

    public PackFixture AddAt(string[] directorySegments, string manifest, params string[] files)
    {
        var directory = Path.Combine([Root, .. directorySegments]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "pack.yaml"), manifest);
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"content of {file}");
        }

        return this;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

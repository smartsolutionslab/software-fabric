using System.Text.RegularExpressions;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

/// <summary>
/// A copy of a real pack whose <c>templates.functions</c> define the vocabulary as plain calls that name the function
/// and its arguments (<c>should_be(total, 42)</c>), standing in for another test framework, assertion library or
/// integration fixture; or, with <see cref="WithoutFunctions"/>, the real pack defining none of it.
/// </summary>
internal sealed partial class VocabularyProbe : IDisposable
{
    public const string FunctionsSlot = "templates.functions";

    private readonly string directory = Directory.CreateTempSubdirectory("fabric-vocabulary-").FullName;

    public VocabularyProbe(PackManifest real, IReadOnlyList<string> vocabulary)
    {
        foreach (var file in Directory.EnumerateFiles(real.Directory, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(directory, Path.GetRelativePath(real.Directory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        const string Functions = "templates/vocabulary-probe.sbn";
        File.WriteAllText(
            Path.Combine(directory, Functions),
            string.Join('\n', vocabulary.Select(name => $"func {name}\n  ret \"{name}(\" + ($ | array.join \", \") + \")\"\nend")));
        Manifest = real with
        {
            Directory = directory,
            Slots = [.. real.Slots.Where(contribution => contribution.Slot != FunctionsSlot), new SlotContribution(FunctionsSlot, Functions, 0)],
        };
    }

    public PackManifest Manifest { get; }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>
    /// The pack as it is, but with nothing in <see cref="FunctionsSlot"/>: a provider that lacks the whole vocabulary.
    /// Its files and other slots stay the real pack's, so what fails to generate is the vocabulary alone.
    /// </summary>
    public static PackManifest WithoutFunctions(PackManifest real) =>
        real with { Slots = [.. real.Slots.Where(contribution => contribution.Slot != FunctionsSlot)] };

    /// <summary>The names of the functions a pack defines in <see cref="FunctionsSlot"/>.</summary>
    public static IReadOnlyList<string> Functions(PackManifest pack) =>
    [
        .. pack.Slots
            .Where(contribution => contribution.Slot == FunctionsSlot)
            .SelectMany(contribution => FunctionDefinition().Matches(File.ReadAllText(Path.Combine(pack.Directory, contribution.Template))))
            .Select(match => match.Groups[1].Value),
    ];

    [GeneratedRegex(@"(?m)^func ([a-z_]+)")]
    private static partial Regex FunctionDefinition();
}

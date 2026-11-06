using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Packs;

namespace Fabric.Packs.Tests;

internal static class Repository
{
    public static string Root { get; } = Locate();

    public static CatalogModel Catalog { get; } = CatalogReader.ReadFile(Path.Combine(Root, "catalog", "catalog.yaml"));

    public static PackLoadResult Packs { get; } = PackLoader.Load(Path.Combine(Root, "packs"), Catalog);

    /// <summary>The catalog's profiles whose resolved project choices include <paramref name="option"/> of <paramref name="dimension"/>.</summary>
    public static IReadOnlyList<string> ProfilesChoosing(string dimension, string option) =>
        [.. Catalog.Profiles.Keys.Where(profile => Chooses(profile, dimension, option))];

    /// <summary>
    /// The catalog's profiles whose resolved project choices do not include <paramref name="option"/> of
    /// <paramref name="dimension"/>, whether the dimension holds another option or is not active.
    /// </summary>
    public static IReadOnlyList<string> ProfilesNotChoosing(string dimension, string option) =>
        [.. Catalog.Profiles.Keys.Where(profile => !Chooses(profile, dimension, option))];

    private static bool Chooses(string profile, string dimension, string option)
    {
        var choices = ChoiceResolver.Resolve(Catalog, profile, []).Choices
            ?? throw new InvalidOperationException($"profile '{profile}' does not resolve.");
        return choices.Project.TryGetValue(dimension, out var options) && options.Contains(option);
    }

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fabric.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Fabric.slnx not found above the test output directory.");
    }
}

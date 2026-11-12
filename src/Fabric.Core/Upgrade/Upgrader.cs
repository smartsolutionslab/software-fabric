using System.Globalization;
using System.Text;
using Fabric.Core.Catalog;
using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Core.Upgrade;

/// <summary>
/// <c>fabric upgrade</c> (ADR-0016 E4): regenerates the project's <c>fabric.yaml</c> with this generator (theirs) and with
/// the version it records (base), and three-way merges theirs into the project with base as the merge base (D8). Every
/// check runs before the project is touched; a refusal leaves it as it was.
/// </summary>
public static class Upgrader
{
    public static UpgradeResult Upgrade(
        string projectDirectory,
        string? from,
        CatalogModel catalog,
        IReadOnlyDictionary<string, PackManifest> packs)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(packs);

        var fabricYamlPath = Path.Combine(projectDirectory, FabricFile.FileName);
        if (!File.Exists(fabricYamlPath))
        {
            return Failure($"no {FabricFile.FileName} in {projectDirectory}; run upgrade in a generated project or pass --project.");
        }

        FabricRecord record;
        using (var reader = File.OpenText(fabricYamlPath))
        {
            record = FabricFile.Read(reader, fabricYamlPath);
        }

        var (repository, gitError) = GitRepository.Open(projectDirectory);
        if (repository is null)
        {
            return Failure(gitError!);
        }

        if (Unreproducible(record) is { } refusal)
        {
            return Failure(refusal);
        }

        var recorded = record.Generator!;
        var theirs = Generator.Regenerate(catalog, packs, record);
        if (theirs.Errors.Count > 0)
        {
            return new UpgradeResult(null, [$"fabric {GeneratorVersion.Current} cannot regenerate {FabricFile.FileName}:", .. theirs.Errors]);
        }

        var scratch = Directory.CreateTempSubdirectory("fabric-upgrade-").FullName;
        try
        {
            var (fabric, installErrors) = from is null ? GeneratorInstaller.Install(recorded, scratch) : (from, []);
            if (fabric is null)
            {
                return new UpgradeResult(null, installErrors);
            }

            var old = new OldGenerator(fabric);
            var (baseDirectory, baseError) = WriteBase(old, record, Path.Combine(scratch, "base", record.Name));
            if (baseDirectory is null)
            {
                return Failure(baseError!);
            }

            var theirsDirectory = Path.Combine(scratch, "theirs", record.Name);
            if (ProjectWriter.Write(theirsDirectory, theirs.Files) is { } writeError)
            {
                return Failure(writeError);
            }

            var baseTree = repository.WriteTree(baseDirectory, Path.Combine(scratch, "base.index"));
            var theirsTree = repository.WriteTree(theirsDirectory, Path.Combine(scratch, "theirs.index"));
            var outcome = repository.Merge(
                baseTree,
                theirsTree,
                ("project", $"fabric {recorded}", $"fabric {GeneratorVersion.Current}"),
                Path.Combine(scratch, "merge"));
            return new UpgradeResult(UpgradeSummary.Write(record.Name, recorded, GeneratorVersion.Current, outcome), [])
            {
                Experimental = theirs.Experimental,
            };
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>The cases of ADR-0016 D3 that <c>fabric.yaml</c> shows by itself; the catalog hash is checked against the base.</summary>
    private static string? Unreproducible(FabricRecord record) => record.Generator switch
    {
        null => $"{FabricFile.FileName} records no generator version, so the tree that generated the project cannot be reproduced " +
            "and fabric upgrade refuses it (ADR-0016 D3).",
        var version when version.EndsWith("-dev", StringComparison.Ordinal) =>
            $"{FabricFile.FileName} was written by fabric {version}, a development build that is never published, so the tree " +
            "it generated cannot be reproduced and fabric upgrade refuses it (ADR-0016 D3).",
        _ => null,
    };

    /// <summary>
    /// Has the old fabric write the project again from <c>fabric.yaml</c>, after checking it is the recorded version
    /// (ADR-0016 D2), and checks that what it wrote records the same catalog and the same choices (D3).
    /// </summary>
    private static (string? Directory, string? Error) WriteBase(OldGenerator old, FabricRecord record, string directory)
    {
        var recorded = record.Generator!;
        var version = old.Run(Path.GetTempPath(), "--version");
        if (version.ExitCode != 0)
        {
            return (null, $"{old.Path} does not run as fabric: {version.Printed}");
        }

        if (version.Output.Trim() != recorded)
        {
            return (null, $"{old.Path} is fabric {version.Output.Trim()}, but {FabricFile.FileName} was written by fabric {recorded}; " +
                "pass the fabric that wrote it.");
        }

        var regeneration = Regeneration.From(record);
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        List<string> arguments = ["new", directory, "--name", regeneration.Name];
        if (regeneration.Profile is { } profile)
        {
            arguments.AddRange(["--profile", profile]);
        }

        arguments.AddRange(regeneration.Overrides.SelectMany(assignment => (string[])["--set", assignment]));
        var created = old.Run(Path.GetDirectoryName(directory)!, [.. arguments]);
        if (created.ExitCode != 0)
        {
            return (null, $"fabric {recorded} could not regenerate {FabricFile.FileName}: {created.Printed}");
        }

        foreach (var module in regeneration.Modules)
        {
            if (Read(directory).Choices.Modules.ContainsKey(module.Name))
            {
                continue;
            }

            var added = old.Run(
                Path.GetDirectoryName(directory)!,
                ["add-module", module.Name, "--project", directory, .. module.Overrides.SelectMany(assignment => (string[])["--set", assignment])]);
            if (added.ExitCode != 0)
            {
                return (null, $"fabric {recorded} could not regenerate {FabricFile.FileName}: {added.Printed}");
            }
        }

        var written = Read(directory);
        if (written.Catalog != record.Catalog)
        {
            return (null, $"{FabricFile.FileName} records catalog {record.Catalog}, but fabric {recorded} writes catalog {written.Catalog}; " +
                "the project was generated with another catalog, so its tree cannot be reproduced and fabric upgrade refuses it (ADR-0016 D3).");
        }

        if (Choices(written) != Choices(record))
        {
            return (null, $"fabric {recorded} does not write the choices {FabricFile.FileName} records, so the tree that generated " +
                "the project cannot be reproduced and fabric upgrade refuses it (ADR-0016 D3).");
        }

        return (directory, null);
    }

    private static FabricRecord Read(string directory)
    {
        var path = Path.Combine(directory, FabricFile.FileName);
        using var reader = File.OpenText(path);
        return FabricFile.Read(reader, path);
    }

    /// <summary>
    /// What a record chose, as text: two records are the same project when these are equal. The order of keys inside a
    /// mapping is not a choice; the order of the modules is, since they are added in it.
    /// </summary>
    private static string Choices(FabricRecord record)
    {
        var regeneration = Regeneration.From(record);
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"name {regeneration.Name}")
            .AppendLine(CultureInfo.InvariantCulture, $"profile {regeneration.Profile}")
            .AppendLine(string.Join(' ', regeneration.Overrides.Order(StringComparer.Ordinal)));
        foreach (var module in regeneration.Modules)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"module {module.Name} {string.Join(' ', module.Overrides.Order(StringComparer.Ordinal))}");
        }

        return text.ToString();
    }

    private static UpgradeResult Failure(string error) => new(null, [error]);
}

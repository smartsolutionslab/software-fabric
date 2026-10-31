using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fabric.Core.Catalog;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Fabric.Core.Packs;

/// <summary>
/// Loads every <c>pack.yaml</c> under a packs directory. A pack lives at
/// <c>&lt;dimension path&gt;/&lt;option&gt;/pack.yaml</c>, so <c>frontend/ui-library/shadcn/</c> holds option
/// <c>frontend.ui-library.shadcn</c>. Broken packs are reported and left out; loading continues.
/// </summary>
public static class PackLoader
{
    private const string ManifestFileName = "pack.yaml";

    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) },
    };

    public static PackLoadResult Load(string packsRoot, CatalogModel catalog)
    {
        ArgumentNullException.ThrowIfNull(packsRoot);
        ArgumentNullException.ThrowIfNull(catalog);

        packsRoot = Path.GetFullPath(packsRoot);
        List<string> errors = [];
        List<PackManifest> packs = [];
        IEnumerable<string> manifests = Directory.Exists(packsRoot)
            ? Directory.EnumerateFiles(packsRoot, ManifestFileName, SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];

        foreach (var manifestPath in manifests)
        {
            var label = Path.GetRelativePath(packsRoot, manifestPath).Replace('\\', '/');
            var pack = LoadOne(packsRoot, manifestPath, catalog, problem => errors.Add($"{label}: {problem}"));
            if (pack is not null)
            {
                packs.Add(pack);
            }
        }

        OrderedDictionary<string, PackManifest> byOption = [];
        foreach (var pack in packs.OrderBy(pack => pack.Option, StringComparer.Ordinal))
        {
            byOption.Add(pack.Option, pack);
        }

        return new PackLoadResult(byOption, errors);
    }

    private static PackManifest? LoadOne(string packsRoot, string manifestPath, CatalogModel catalog, Action<string> report)
    {
        var directory = Path.GetDirectoryName(manifestPath)!;
        var directoryOption = string.Join('.', Path.GetRelativePath(packsRoot, directory).Split(Path.DirectorySeparatorChar));

        JsonNode? json;
        try
        {
            var stream = new YamlStream();
            using (var reader = File.OpenText(manifestPath))
            {
                stream.Load(reader);
            }

            json = stream.Documents.Count == 1 ? YamlJson.Convert(stream.Documents[0].RootNode) : null;
        }
        catch (YamlException exception)
        {
            report($"line {exception.Start.Line}: {exception.Message}");
            return null;
        }

        var schemaErrors = SchemaErrors(json);
        if (schemaErrors.Count > 0)
        {
            schemaErrors.ForEach(report);
            return null;
        }

        var raw = json.Deserialize<RawManifest>(SerializerOptions)!;
        var pack = new PackManifest(
            raw.Option,
            directory,
            raw.Files ?? [],
            raw.Slots ?? [],
            raw.DeclaresSlots ?? [],
            new PackAgents(raw.Agents.AgentsMd, raw.Agents.Briefs ?? new Dictionary<string, string>()),
            raw.Adr,
            raw.Gates);

        var problems = SemanticProblems(pack, directoryOption, catalog).ToList();
        problems.ForEach(report);
        return problems.Count == 0 ? pack : null;
    }

    private static List<string> SchemaErrors(JsonNode? json)
    {
        var element = JsonSerializer.SerializeToElement(json);
        var result = Schema.Value.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
        {
            return [];
        }

        var failures = (result.Details ?? [result])
            .Where(detail => detail.Errors is { Count: > 0 })
            .SelectMany(detail => detail.Errors!.Values.Select(message => (Location: detail.InstanceLocation.ToString(), Message: message)))
            .ToList();

        // A failing child also fails every parent ("some properties did not match"); report only the deepest.
        return [.. failures
            .Where(failure => !failures.Any(other => other.Location.StartsWith(failure.Location + "/", StringComparison.Ordinal)))
            .Select(failure => $"{Location(failure.Location)}: {failure.Message}")
            .Distinct(StringComparer.Ordinal)];
    }

    private static string Location(string pointer) => pointer.Length == 0 ? "/" : pointer;

    // ADR-0008: other packs contribute to a role slot whichever option of the dimension is chosen, so every option declares it, joined alike.
    private static IEnumerable<string> RoleSlotProblems(PackManifest pack, Dimension dimension)
    {
        foreach (var role in dimension.RoleSlots.Values)
        {
            var declaration = pack.DeclaredSlots.FirstOrDefault(slot => slot.Slot == role.Slot);
            if (declaration is null)
            {
                yield return $"does not declare the role slot '{role.Slot}' that every {dimension.Id} option declares (catalog).";
            }
            else if (declaration.Separator != role.Separator)
            {
                yield return $"declares the role slot '{role.Slot}' joined by {Joiner(declaration.Separator)}; the catalog joins it by {Joiner(role.Separator)}.";
            }
        }
    }

    private static string Joiner(string? separator) =>
        separator is null ? "a newline" : $"'{separator.Replace("\n", "\\n", StringComparison.Ordinal)}'";

    private static IEnumerable<string> SemanticProblems(PackManifest pack, string directoryOption, CatalogModel catalog)
    {
        if (pack.Option != directoryOption)
        {
            yield return $"declares option '{pack.Option}', but its directory says '{directoryOption}'.";
            yield break;
        }

        if (!pack.IsBase)
        {
            if (!catalog.Dimensions.TryGetValue(pack.Dimension, out var dimension))
            {
                yield return $"'{pack.Dimension}' is not a dimension in the catalog.";
            }
            else if (!dimension.Options.ContainsKey(pack.OptionName))
            {
                yield return $"'{pack.OptionName}' is not an option of {pack.Dimension} in the catalog.";
            }
            else
            {
                foreach (var problem in RoleSlotProblems(pack, dimension))
                {
                    yield return problem;
                }
            }
        }

        foreach (var path in ReferencedPaths(pack))
        {
            var fullPath = Path.GetFullPath(Path.Combine(pack.Directory, path));
            if (!fullPath.StartsWith(pack.Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                yield return $"'{path}' points outside the pack.";
            }
            else if (!File.Exists(fullPath))
            {
                yield return $"'{path}' does not exist in the pack.";
            }
        }
    }

    private static IEnumerable<string> ReferencedPaths(PackManifest pack) =>
        pack.Files.Select(file => file.Template)
            .Concat(pack.Slots.Select(slot => slot.Template))
            .Append(pack.Agents.AgentsMd)
            .Concat(pack.Agents.Briefs.Values)
            .Append(pack.Adr)
            .Concat(pack.Gates.Select(gate => gate.File).OfType<string>())
            .Distinct(StringComparer.Ordinal);

    private static JsonSchema LoadSchema()
    {
        using var stream = typeof(PackLoader).Assembly.GetManifestResourceStream("pack.schema.json")
            ?? throw new InvalidOperationException("Embedded resource pack.schema.json is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    private sealed record RawManifest(
        string Option,
        IReadOnlyList<PackFile>? Files,
        IReadOnlyList<SlotContribution>? Slots,
        IReadOnlyList<SlotDeclaration>? DeclaresSlots,
        RawAgents Agents,
        string Adr,
        IReadOnlyList<PackGate> Gates);

    private sealed record RawAgents(string AgentsMd, IReadOnlyDictionary<string, string>? Briefs);
}

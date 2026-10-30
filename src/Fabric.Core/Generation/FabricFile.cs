using System.Text;
using System.Text.RegularExpressions;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Fabric.Core.Generation;

public sealed record FabricRecord(string Name, string? Profile, ProjectChoices Choices);

/// <summary>
/// <c>fabric.yaml</c>: the record of what a generated project chose. Written once by <c>fabric new</c>, extended by
/// <c>fabric add-module</c>, and read by agents and tools. Multi-valued dimensions are always lists.
/// </summary>
public static partial class FabricFile
{
    public const string FileName = "fabric.yaml";

    private const int Version = 1;

    public static string Write(CatalogModel catalog, string name, string? profile, ProjectChoices choices)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(choices);

        var text = new StringBuilder()
            .Append("# Written by fabric. These choices are fixed for this project (ADR-0001 of the generator).\n")
            .Append("fabric: ").Append(Version).Append('\n')
            .Append("name: ").Append(Scalar(name)).Append('\n');
        if (profile is not null)
        {
            text.Append("profile: ").Append(Scalar(profile)).Append('\n');
        }

        AppendValues(text, catalog, "project", choices.Project, indent: string.Empty);
        AppendValues(text, catalog, "module-defaults", choices.ModuleDefaults, indent: string.Empty);

        if (choices.Modules.Count == 0)
        {
            text.Append("modules: {}\n");
        }
        else
        {
            text.Append("modules:\n");
            foreach (var (moduleName, values) in choices.Modules)
            {
                AppendValues(text, catalog, moduleName, values, indent: "  ");
            }
        }

        return text.ToString();
    }

    public static FabricRecord Read(TextReader reader, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sourceName);

        var stream = new YamlStream();
        try
        {
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            throw new FabricFileException($"{sourceName}:{exception.Start.Line}: {exception.Message}", exception);
        }

        if (stream.Documents.Count != 1)
        {
            throw new FabricFileException($"{sourceName}: expected exactly one YAML document.");
        }

        var source = new YamlSource(sourceName, message => new FabricFileException(message));
        var root = source.Mapping(stream.Documents[0].RootNode, FileName);
        var fields = source.Fields(root, FileName, "fabric", "name", "profile", "project", "module-defaults", "modules");

        var versionNode = fields.Required("fabric");
        var version = source.Integer(versionNode, "fabric", FileName);
        if (version != Version)
        {
            throw source.Error(versionNode, $"version {version} is not supported; this fabric reads version {Version}.");
        }

        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> modules = [];
        foreach (var (key, value) in source.Mapping(fields.Required("modules"), "'modules'").Children)
        {
            var moduleName = source.Scalar(key, "a module name");
            modules.Add(moduleName, ReadValues(source, value, $"module '{moduleName}'"));
        }

        return new FabricRecord(
            source.Scalar(fields.Required("name"), FileName),
            fields.Optional("profile") is { } profile ? source.Scalar(profile, FileName) : null,
            new ProjectChoices(
                ReadValues(source, fields.Required("project"), "'project'"),
                ReadValues(source, fields.Required("module-defaults"), "'module-defaults'"),
                modules));
    }

    private static void AppendValues(
        StringBuilder text,
        CatalogModel catalog,
        string key,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        string indent)
    {
        if (values.Count == 0)
        {
            text.Append(indent).Append(key).Append(": {}\n");
            return;
        }

        text.Append(indent).Append(key).Append(":\n");
        foreach (var (dimension, chosen) in values)
        {
            var multi = catalog.Dimensions.TryGetValue(dimension, out var definition) && definition.Multi;
            var value = multi ? $"[{string.Join(", ", chosen.Select(Scalar))}]" : Scalar(chosen.Single());
            text.Append(indent).Append("  ").Append(dimension).Append(": ").Append(value).Append('\n');
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadValues(YamlSource source, YamlNode node, string owner) =>
        source.Mapping(node, owner).Children.ToDictionary(
            entry => source.Scalar(entry.Key, owner),
            entry => source.ScalarOrList(entry.Value, owner));

    /// <summary>Quotes what a YAML 1.1 reader would take for a boolean, null or number.</summary>
    private static string Scalar(string value) => AmbiguousScalar().IsMatch(value) ? $"\"{value}\"" : value;

    [GeneratedRegex(@"^(?:y|yes|n|no|true|false|on|off|null|~|[-+.]?\d.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AmbiguousScalar();
}

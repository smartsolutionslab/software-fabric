using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Fabric.Core.Catalog;

/// <summary>Reads <c>catalog.yaml</c>. Structure only: whether the rules make sense is checked elsewhere.</summary>
public static class CatalogReader
{
    public static CatalogModel ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var reader = File.OpenText(path);
        return Read(reader, path);
    }

    public static CatalogModel Read(TextReader reader, string sourceName)
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
            throw new CatalogException($"{sourceName}:{exception.Start.Line}: {exception.Message}", exception);
        }

        if (stream.Documents.Count != 1)
        {
            throw new CatalogException($"{sourceName}: expected exactly one YAML document.");
        }

        var source = new YamlSource(sourceName);
        var root = source.Mapping(stream.Documents[0].RootNode, "the catalog");
        var fields = source.Fields(root, "the catalog", "version", "profiles", "dimensions");

        var version = source.Integer(fields.Required("version"), "version", "the catalog");
        var dimensions = ReadDimensions(source, fields.Required("dimensions"));
        var profiles = fields.Optional("profiles") is { } profilesNode
            ? ReadProfiles(source, profilesNode)
            : new OrderedDictionary<string, Profile>();

        return new CatalogModel(version, dimensions, profiles);
    }

    private static OrderedDictionary<string, Dimension> ReadDimensions(YamlSource source, YamlNode node)
    {
        OrderedDictionary<string, Dimension> dimensions = [];
        foreach (var (key, value) in source.Mapping(node, "'dimensions'").Children)
        {
            var id = source.Scalar(key, "a dimension id");
            dimensions.Add(id, ReadDimension(source, id, value));
        }

        return dimensions;
    }

    private static Dimension ReadDimension(YamlSource source, string id, YamlNode node)
    {
        var owner = $"dimension '{id}'";
        var fields = source.Fields(
            source.Mapping(node, owner),
            owner,
            "scope",
            "multi",
            "active-when",
            "default",
            "note",
            "options");

        OrderedDictionary<string, CatalogOption> options = [];
        foreach (var (key, value) in source.Mapping(fields.Required("options"), $"'options' of {owner}").Children)
        {
            var optionId = source.Scalar(key, $"an option id of {owner}");
            options.Add(optionId, ReadOption(source, $"{id}.{optionId}", optionId, value));
        }

        return new Dimension(
            id,
            source.Enum<DimensionScope>(fields.Required("scope"), "scope", owner),
            fields.Optional("multi") is { } multi && source.Boolean(multi, "multi", owner),
            fields.Optional("active-when") is { } activeWhen ? source.Scalar(activeWhen, owner) : null,
            fields.Optional("default") is { } defaultNode ? source.ScalarOrList(defaultNode, owner) : null,
            fields.Optional("note") is { } note ? source.Scalar(note, owner) : null,
            options);
    }

    private static CatalogOption ReadOption(YamlSource source, string qualifiedId, string id, YamlNode node)
    {
        var owner = $"option '{qualifiedId}'";
        var fields = source.Fields(source.Mapping(node, owner), owner, "tier", "requires", "conflicts", "note");

        return new CatalogOption(
            id,
            source.Enum<SupportTier>(fields.Required("tier"), "tier", owner),
            fields.Optional("requires") is { } requires ? source.List(requires, owner) : [],
            fields.Optional("conflicts") is { } conflicts ? source.List(conflicts, owner) : [],
            fields.Optional("note") is { } note ? source.Scalar(note, owner) : null);
    }

    private static OrderedDictionary<string, Profile> ReadProfiles(YamlSource source, YamlNode node)
    {
        OrderedDictionary<string, Profile> profiles = [];
        foreach (var (key, value) in source.Mapping(node, "'profiles'").Children)
        {
            var id = source.Scalar(key, "a profile id");
            var owner = $"profile '{id}'";
            string? extends = null;
            OrderedDictionary<string, IReadOnlyList<string>> settings = [];

            foreach (var (settingKey, settingValue) in source.Mapping(value, owner).Children)
            {
                var dimension = source.Scalar(settingKey, owner);
                if (dimension == "extends")
                {
                    extends = source.Scalar(settingValue, owner);
                }
                else
                {
                    settings.Add(dimension, source.ScalarOrList(settingValue, owner));
                }
            }

            profiles.Add(id, new Profile(id, extends, settings));
        }

        return profiles;
    }
}

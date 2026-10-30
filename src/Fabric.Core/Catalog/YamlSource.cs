using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Fabric.Core.Catalog;

/// <summary>Reads YAML nodes strictly and reports every problem with file and line.</summary>
internal sealed class YamlSource(string name)
{
    public CatalogException Error(YamlNode node, string message) =>
        new($"{name}:{node.Start.Line}: {message}");

    public YamlMappingNode Mapping(YamlNode node, string owner) =>
        node as YamlMappingNode ?? throw Error(node, $"{owner} must be a mapping.");

    public string Scalar(YamlNode node, string owner) =>
        node is YamlScalarNode { Value: { } value } ? value : throw Error(node, $"{owner} must be a single value.");

    public IReadOnlyList<string> List(YamlNode node, string owner) =>
        node is YamlSequenceNode sequence
            ? [.. sequence.Children.Select(child => Scalar(child, owner))]
            : throw Error(node, $"{owner} must be a list.");

    public IReadOnlyList<string> ScalarOrList(YamlNode node, string owner) =>
        node is YamlSequenceNode ? List(node, owner) : [Scalar(node, owner)];

    public Condition Condition(YamlNode node, string owner)
    {
        var text = Scalar(node, owner);
        try
        {
            return ConditionParser.Parse(text);
        }
        catch (ConditionSyntaxException exception)
        {
            throw Error(node, $"invalid condition on {owner}: {exception.Message}");
        }
    }

    public IReadOnlyList<Condition> Conditions(YamlNode node, string owner) =>
        node is YamlSequenceNode sequence
            ? [.. sequence.Children.Select(child => Condition(child, owner))]
            : throw Error(node, $"{owner} must be a list.");

    public int Integer(YamlNode node, string field, string owner)
    {
        var value = Scalar(node, owner);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Error(node, $"invalid value '{value}' for '{field}' on {owner}; expected a whole number.");
    }

    public bool Boolean(YamlNode node, string field, string owner) =>
        Scalar(node, owner) switch
        {
            "true" => true,
            "false" => false,
            var value => throw Error(node, $"invalid value '{value}' for '{field}' on {owner}; expected true or false."),
        };

    public TEnum Enum<TEnum>(YamlNode node, string field, string owner)
        where TEnum : struct, Enum
    {
        var value = Scalar(node, owner);
        var names = System.Enum.GetNames<TEnum>().Select(name => name.ToLowerInvariant()).ToList();
        var index = names.IndexOf(value);
        return index >= 0
            ? System.Enum.GetValues<TEnum>()[index]
            : throw Error(node, $"invalid value '{value}' for '{field}' on {owner}; expected {string.Join(" or ", names)}.");
    }

    /// <summary>The entries of a mapping, rejecting any key outside <paramref name="allowed"/>.</summary>
    public Fields Fields(YamlMappingNode mapping, string owner, params string[] allowed)
    {
        foreach (var key in mapping.Children.Keys)
        {
            var field = Scalar(key, owner);
            if (!allowed.Contains(field))
            {
                throw Error(key, $"unknown field '{field}' on {owner}; allowed: {string.Join(", ", allowed)}.");
            }
        }

        return new Fields(this, mapping, owner);
    }
}

internal sealed class Fields(YamlSource source, YamlMappingNode mapping, string owner)
{
    public YamlNode? Optional(string field) =>
        mapping.Children.TryGetValue(new YamlScalarNode(field), out var node) ? node : null;

    public YamlNode Required(string field) =>
        Optional(field) ?? throw source.Error(mapping, $"missing field '{field}' on {owner}.");
}

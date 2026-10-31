using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Fabric.Core.Packs;

/// <summary>
/// Converts YAML to JSON so a YAML file can be checked against a JSON schema. Only plain (unquoted) scalars are
/// typed: <c>true</c>/<c>false</c> become booleans, whole numbers become numbers, <c>null</c>/<c>~</c> become null.
/// </summary>
internal static class YamlJson
{
    public static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode mapping => new JsonObject(mapping.Children.Select(entry =>
            KeyValuePair.Create(((YamlScalarNode)entry.Key).Value ?? string.Empty, Convert(entry.Value)))),
        YamlSequenceNode sequence => new JsonArray([.. sequence.Children.Select(Convert)]),
        YamlScalarNode scalar => Scalar(scalar),
        _ => throw new InvalidOperationException($"Unsupported YAML node {node.NodeType}."),
    };

    private static JsonValue? Scalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        return value switch
        {
            "true" => JsonValue.Create(true),
            "false" => JsonValue.Create(false),
            "null" or "~" or "" => null,
            _ when long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
            _ => JsonValue.Create(value),
        };
    }
}

using YamlDotNet.RepresentationModel;

namespace Fabric.Core.Catalog;

internal sealed class Fields(YamlSource source, YamlMappingNode mapping, string owner)
{
    public YamlNode? Optional(string field) =>
        mapping.Children.TryGetValue(new YamlScalarNode(field), out var node) ? node : null;

    public YamlNode Required(string field) =>
        Optional(field) ?? throw source.Error(mapping, $"missing field '{field}' on {owner}.");
}
